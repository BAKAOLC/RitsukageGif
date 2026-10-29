using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RitsukageGif.Scripting
{
    public sealed class ScriptLanguageServer(
        Func<string[], Task<JsonNode?>>? command = null,
        ScriptBindingRegistry? bindings = null)
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Scripting.LanguageServer");
        private readonly ScriptBindingRegistry _bindings = bindings ?? CaptureScriptLibrary.CreateBindings();
        private readonly Dictionary<string, string> _documents = new();
        private readonly Dictionary<string, ScriptSemanticModel> _models = new();

        private readonly Dictionary<string, string> _openFiles =
            new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        private readonly HashSet<string> _publishedDiagnostics = [];

        private ScriptSourceResolver Sources =>
            field ??= new(path => _openFiles.GetValueOrDefault(path));

        private static string? FilePath(string uri)
        {
            return Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile ? parsed.LocalPath : null;
        }

        private static JsonObject Position(int line, int column)
        {
            return new()
                { ["line"] = Math.Max(0, line - 1), ["character"] = Math.Max(0, column - 1) };
        }

        private static JsonObject Range(int line, int column, int length = 1)
        {
            return new()
                { ["start"] = Position(line, column), ["end"] = Position(line, column + length) };
        }

        public async Task RunAsync(Stream input, Stream output, CancellationToken cancellation = default)
        {
            Logger.ZLogInformation($"Language server protocol session started.");
            try
            {
                await RunCoreAsync(input, output, cancellation);
            }
            catch (OperationCanceledException)
            {
                Logger.ZLogInformation($"Language server protocol session canceled.");
                throw;
            }
            catch (Exception error)
            {
                Logger.ZLogError(
                    $"Language server protocol session failed. ExceptionType={error.GetType().Name} StackTrace={error.StackTrace}");
                throw;
            }
            finally
            {
                Logger.ZLogInformation(
                    $"Language server protocol session ended. OpenDocumentCount={_documents.Count} Canceled={cancellation.IsCancellationRequested}");
            }
        }

        private async Task RunCoreAsync(Stream input, Stream output, CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                var header = new List<byte>();
                var one = new byte[1];
                while (header.Count < 8192)
                {
                    if (await input.ReadAsync(one, cancellation) == 0)
                    {
                        Logger.ZLogDebug($"Language server input reached end of stream.");
                        return;
                    }

                    header.Add(one[0]);
                    if (CollectionsMarshal.AsSpan(header).EndsWith("\r\n\r\n"u8)) break;
                }

                var lengthHeader = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n")
                    .FirstOrDefault(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                if (lengthHeader is null || !int.TryParse(lengthHeader[15..].Trim(), out var length) ||
                    length is < 0 or > 2 * 1024 * 1024)
                {
                    Logger.ZLogWarning(
                        $"Language server rejected an invalid content length header. HeaderByteCount={header.Count}");
                    return;
                }

                var body = new byte[length];
                await input.ReadExactlyAsync(body, cancellation);
                JsonNode? message;
                try
                {
                    message = JsonNode.Parse(body);
                }
                catch (JsonException)
                {
                    Logger.ZLogWarning($"Language server rejected an invalid JSON frame. BodyByteCount={body.Length}");
                    continue;
                }

                var id = message?["id"]?.DeepClone();
                var method = message?["method"]?.GetValue<string>();
                var args = message?["params"];
                if (method == "exit")
                {
                    Logger.ZLogInformation($"Language server exit requested.");
                    return;
                }

                try
                {
                    switch (method)
                    {
                        case "textDocument/didOpen" or "textDocument/didChange":
                        {
                            var uri = args!["textDocument"]!["uri"]!.GetValue<string>();
                            var text = method.EndsWith("didOpen", StringComparison.Ordinal)
                                ? args["textDocument"]!["text"]!.GetValue<string>()
                                : args["contentChanges"]![0]!["text"]!.GetValue<string>();
                            _documents[uri] = text;
                            Logger.ZLogDebug(
                                $"Language server document updated. CharacterCount={text.Length} OpenDocumentCount={_documents.Count}");
                            if (FilePath(uri) is { } file) _openFiles[file] = text;
                            _models.Clear();
                            await PublishDiagnosticsAsync(output, cancellation);
                            continue;
                        }
                        case "textDocument/didClose":
                        {
                            var uri = args!["textDocument"]!["uri"]!.GetValue<string>();
                            _documents.Remove(uri);
                            Logger.ZLogDebug($"Language server document closed. OpenDocumentCount={_documents.Count}");
                            if (FilePath(uri) is { } file) _openFiles.Remove(file);
                            _models.Clear();
                            await PublishDiagnosticsAsync(output, cancellation);
                            continue;
                        }
                    }

                    if (id is null) continue;
                    var result = await HandleAsync(method ?? "", args);
                    await SendAsync(output, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result },
                        cancellation);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Logger.ZLogWarning(
                        $"Language server request failed. ExceptionType={ex.GetType().Name} ResponseExpected={id is not null}");
                    if (id is not null)
                        await SendAsync(output,
                            new JsonObject
                            {
                                ["jsonrpc"] = "2.0", ["id"] = id,
                                ["error"] = new JsonObject
                                    { ["code"] = -32603, ["message"] = ex.Message, ["data"] = ErrorDetails(ex) },
                            }, cancellation);
                }
            }
        }

        private async Task PublishDiagnosticsAsync(Stream output, CancellationToken token)
        {
            var grouped = _documents.Keys.ToDictionary(uri => uri, _ => new List<ScriptDiagnostic>());
            foreach (var document in _documents)
            {
                var file = FilePath(document.Key);
                var model = new ScriptSemanticModel(document.Value, _bindings, file, Sources);
                _models[document.Key] = model;
                foreach (var diagnostic in PresetLanguage.Diagnose(document.Value, _bindings, file)
                             .Concat(model.Diagnostics))
                {
                    var uri = diagnostic.Source is { } source
                        ? new Uri(Path.GetFullPath(source)).AbsoluteUri
                        : document.Key;
                    if (!grouped.TryGetValue(uri, out var diagnostics)) grouped[uri] = diagnostics = [];
                    var existingIndex = diagnostics.FindIndex(existing =>
                        existing.Line == diagnostic.Line && existing.Column == diagnostic.Column &&
                        existing.Message == diagnostic.Message);
                    if (existingIndex < 0) diagnostics.Add(diagnostic);
                    else
                        diagnostics[existingIndex] = diagnostics[existingIndex] with
                        {
                            Related =
                            [
                                .. diagnostics[existingIndex].Related.Concat(diagnostic.Related)
                                    .DistinctBy(item => (item.Source, item.Line, item.Column, item.Message)),
                            ],
                        };
                }
            }

            foreach (var uri in _publishedDiagnostics.Except(grouped.Keys).ToArray()) grouped[uri] = [];
            Logger.ZLogDebug(
                $"Language server diagnostics publishing. DocumentCount={grouped.Count} DiagnosticCount={grouped.Values.Sum(items => items.Count)}");
            _publishedDiagnostics.Clear();
            foreach (var group in grouped)
            {
                if (group.Value.Count > 0) _publishedDiagnostics.Add(group.Key);
                var diagnostics = new JsonArray(group.Value.Select(JsonNode (diagnostic) => new JsonObject
                {
                    ["range"] = Range(diagnostic.Line, diagnostic.Column), ["severity"] = 1,
                    ["source"] = "rgscript", ["message"] = diagnostic.Message,
                    ["relatedInformation"] = new JsonArray(diagnostic.Related
                        .Where(location => location.Source is not null)
                        .Select(JsonNode (location) => new JsonObject
                        {
                            ["location"] = new JsonObject
                            {
                                ["uri"] = new Uri(Path.GetFullPath(location.Source!)).AbsoluteUri,
                                ["range"] = Range(location.Line, location.Column),
                            },
                            ["message"] = location.Message,
                        }).ToArray()),
                }).ToArray());
                await SendAsync(output, new JsonObject
                {
                    ["jsonrpc"] = "2.0", ["method"] = "textDocument/publishDiagnostics",
                    ["params"] = new JsonObject { ["uri"] = group.Key, ["diagnostics"] = diagnostics },
                }, token);
            }
        }

        private static JsonObject? ErrorDetails(Exception error)
        {
            var diagnostic = error switch
            {
                ScriptExecutionException execution => execution.Diagnostic,
                PresetSyntaxException syntax => syntax.Diagnostic,
                _ => null,
            };
            if (diagnostic is null) return null;
            return new()
            {
                ["source"] = diagnostic.Source, ["line"] = diagnostic.Line, ["column"] = diagnostic.Column,
                ["message"] = diagnostic.Message,
                ["frames"] = new JsonArray((error is ScriptExecutionException runtimeError ? runtimeError.Frames : [])
                    .Select(JsonNode (frame) => new JsonObject
                    {
                        ["name"] = frame.Name, ["source"] = frame.Source, ["line"] = frame.Line,
                        ["column"] = frame.Column,
                    }).ToArray()),
            };
        }

        private async Task<JsonNode?> HandleAsync(string method, JsonNode? args)
        {
            switch (method)
            {
                case "initialize":
                    Logger.ZLogInformation($"Language server initialized.");
                    return new JsonObject
                    {
                        ["capabilities"] = new JsonObject
                        {
                            ["textDocumentSync"] = 1,
                            ["completionProvider"] = new JsonObject { ["triggerCharacters"] = new JsonArray(".") },
                            ["signatureHelpProvider"] = new JsonObject
                                { ["triggerCharacters"] = new JsonArray("(", ",") },
                            ["hoverProvider"] = true, ["definitionProvider"] = true, ["documentSymbolProvider"] = true,
                        },
                    };
                case "shutdown":
                    Logger.ZLogInformation($"Language server shutdown requested.");
                    return null;
                case "rgscript/executableLines":
                    return new JsonArray([
                        .. PresetLanguage.ExecutableLines(args!["text"]!.GetValue<string>())
                            .Select(JsonNode (n) => JsonValue.Create(n)),
                    ]);
                case "rgscript/command":
                {
                    if (command is null) throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                    return await command([.. args!["arguments"]!.AsArray().Select(v => v!.GetValue<string>())]);
                }
            }

            var uri = args?["textDocument"]?["uri"]?.GetValue<string>() ?? "";
            var source = _documents.GetValueOrDefault(uri) ?? "";
            var model = _models.GetValueOrDefault(uri)?.Refresh() ??
                        new ScriptSemanticModel(source, _bindings, FilePath(uri), Sources);
            _models[uri] = model;
            var row = (args?["position"]?["line"]?.GetValue<int>() ?? 0) + 1;
            var column = (args?["position"]?["character"]?.GetValue<int>() ?? 0) + 1;
            var lines = source.Split('\n');
            var offset = Math.Min(source.Length, lines.Take(row - 1).Sum(l => l.Length + 1) + column - 1);
            switch (method)
            {
                case "textDocument/completion":
                    return new JsonArray(model.Complete(offset).Select(JsonNode (c) => new JsonObject
                        {
                            ["label"] = c.Label, ["insertText"] = c.InsertText, ["detail"] = c.Detail,
                            ["kind"] = c.Kind switch
                            {
                                "method" => 2, "function" => 3, "variable" => 6, "property" => 10, "enumMember" => 20,
                                "class" => 7, "module" => 9, "enum" => 13, _ => 14,
                            },
                        })
                        .ToArray());
                case "textDocument/documentSymbol":
                    return new JsonArray(model.Symbols.Select(JsonNode (s) => new JsonObject
                    {
                        ["name"] = s.Name, ["kind"] = s.Kind switch { "fn" => 12, "let" or "const" => 13, _ => 5 },
                        ["range"] = Range(s.Line, s.Column, s.Name.Length),
                        ["selectionRange"] = Range(s.Line, s.Column, s.Name.Length),
                    }).ToArray());
                case "textDocument/signatureHelp":
                {
                    var signature = model.Signature(offset);
                    return signature is null
                        ? null
                        : new JsonObject
                        {
                            ["signatures"] = new JsonArray(new JsonObject
                            {
                                ["label"] = signature.Label, ["documentation"] = signature.Documentation,
                                ["parameters"] = new JsonArray(signature.Parameters
                                    .Select(JsonNode (p) => new JsonObject { ["label"] = p }).ToArray()),
                            }),
                            ["activeSignature"] = 0, ["activeParameter"] = signature.ActiveParameter,
                        };
                }
            }

            var word = PresetLanguage.WordAt(source, offset);
            if (word is null) return null;
            switch (method)
            {
                case "textDocument/hover":
                    return new JsonObject
                    {
                        ["contents"] = new JsonObject
                            { ["kind"] = "plaintext", ["value"] = model.Hint(offset) ?? word.Text },
                        ["range"] = Range(word.Line, word.Column, word.Length),
                    };
                case "textDocument/definition":
                {
                    var symbol = model.Definition(offset);
                    return symbol is null
                        ? null
                        : new JsonObject
                        {
                            ["uri"] = symbol.File is { } file ? new Uri(file).AbsoluteUri : uri,
                            ["range"] = Range(symbol.Line, symbol.Column, symbol.Name.Length),
                        };
                }
            }

            return null;
        }

        private static async Task SendAsync(Stream output, JsonNode message, CancellationToken token)
        {
            await using var buffer = new MemoryStream();
            await using (var writer = new Utf8JsonWriter(buffer)) message.WriteTo(writer);
            var bytes = buffer.ToArray();
            await output.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n"), token);
            await output.WriteAsync(bytes, token);
            await output.FlushAsync(token);
        }
    }
}

using System.Text;
using System.Text.Json.Nodes;

namespace RitsukageGif.Scripting
{
    public sealed class ScriptLanguageServer(
        Func<string[], Task<JsonNode?>>? command = null,
        ScriptBindingRegistry? bindings = null)
    {
        private readonly Dictionary<string, string> _documents = new();
        private readonly Dictionary<string, ScriptSemanticModel> _models = new();
        private readonly HashSet<string> _publishedDiagnostics = [];

        private readonly Dictionary<string, string> _openFiles =
            new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        private ScriptSourceResolver? _sources;

        private ScriptSourceResolver Sources =>
            _sources ??= new ScriptSourceResolver(path => _openFiles.GetValueOrDefault(path));

        private static string? FilePath(string uri) =>
            Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile ? parsed.LocalPath : null;

        private readonly ScriptBindingRegistry _bindings = bindings ?? CaptureScriptLibrary.CreateBindings();

        private static JsonObject Position(int line, int column) => new()
            { ["line"] = Math.Max(0, line - 1), ["character"] = Math.Max(0, column - 1) };

        private static JsonObject Range(int line, int column, int length = 1) => new()
            { ["start"] = Position(line, column), ["end"] = Position(line, column + length) };

        public async Task RunAsync(Stream input, Stream output, CancellationToken cancellation = default)
        {
            while (!cancellation.IsCancellationRequested)
            {
                var header = new List<byte>();
                var one = new byte[1];
                while (header.Count < 8192)
                {
                    if (await input.ReadAsync(one, cancellation) == 0) return;
                    header.Add(one[0]);
                    if (header.Count >= 4 && header.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) break;
                }

                var lengthHeader = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n")
                    .FirstOrDefault(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                if (!int.TryParse(lengthHeader is null ? null : lengthHeader[15..].Trim(), out var length) ||
                    length is < 0 or > 2 * 1024 * 1024) return;
                var body = new byte[length];
                await input.ReadExactlyAsync(body, cancellation);
                JsonNode? message;
                try
                {
                    message = JsonNode.Parse(body);
                }
                catch (System.Text.Json.JsonException)
                {
                    continue;
                }

                var id = message?["id"]?.DeepClone();
                var method = message?["method"]?.GetValue<string>();
                var args = message?["params"];
                if (method == "exit") return;
                try
                {
                    if (method is "textDocument/didOpen" or "textDocument/didChange")
                    {
                        var uri = args!["textDocument"]!["uri"]!.GetValue<string>();
                        var text = method.EndsWith("didOpen", StringComparison.Ordinal)
                            ? args["textDocument"]!["text"]!.GetValue<string>()
                            : args["contentChanges"]![0]!["text"]!.GetValue<string>();
                        _documents[uri] = text;
                        if (FilePath(uri) is { } file) _openFiles[file] = text;
                        _models.Clear();
                        await PublishDiagnosticsAsync(output, cancellation);
                        continue;
                    }

                    if (method == "textDocument/didClose")
                    {
                        var uri = args!["textDocument"]!["uri"]!.GetValue<string>();
                        _documents.Remove(uri);
                        if (FilePath(uri) is { } file) _openFiles.Remove(file);
                        _models.Clear();
                        await PublishDiagnosticsAsync(output, cancellation);
                        continue;
                    }

                    if (id is null) continue;
                    var result = await HandleAsync(method ?? "", args);
                    await SendAsync(output, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result },
                        cancellation);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (id is not null)
                        await SendAsync(output,
                            new JsonObject
                            {
                                ["jsonrpc"] = "2.0", ["id"] = id,
                                ["error"] = new JsonObject
                                    { ["code"] = -32603, ["message"] = ex.Message, ["data"] = ErrorDetails(ex) }
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
                            Related = diagnostics[existingIndex].Related.Concat(diagnostic.Related)
                                .DistinctBy(item => (item.Source, item.Line, item.Column, item.Message)).ToArray()
                        };
                }
            }

            foreach (var uri in _publishedDiagnostics.Except(grouped.Keys).ToArray()) grouped[uri] = [];
            _publishedDiagnostics.Clear();
            foreach (var group in grouped)
            {
                if (group.Value.Count > 0) _publishedDiagnostics.Add(group.Key);
                var diagnostics = new JsonArray(group.Value.Select(diagnostic => (JsonNode)new JsonObject
                {
                    ["range"] = Range(diagnostic.Line, diagnostic.Column), ["severity"] = 1,
                    ["source"] = "rgscript", ["message"] = diagnostic.Message,
                    ["relatedInformation"] = new JsonArray(diagnostic.Related
                        .Where(location => location.Source is not null)
                        .Select(location => (JsonNode)new JsonObject
                        {
                            ["location"] = new JsonObject
                            {
                                ["uri"] = new Uri(Path.GetFullPath(location.Source!)).AbsoluteUri,
                                ["range"] = Range(location.Line, location.Column)
                            },
                            ["message"] = location.Message
                        }).ToArray())
                }).ToArray());
                await SendAsync(output, new JsonObject
                {
                    ["jsonrpc"] = "2.0", ["method"] = "textDocument/publishDiagnostics",
                    ["params"] = new JsonObject { ["uri"] = group.Key, ["diagnostics"] = diagnostics }
                }, token);
            }
        }

        private static JsonObject? ErrorDetails(Exception error)
        {
            var diagnostic = error switch
            {
                ScriptExecutionException execution => execution.Diagnostic,
                PresetSyntaxException syntax => syntax.Diagnostic,
                _ => null
            };
            if (diagnostic is null) return null;
            return new JsonObject
            {
                ["source"] = diagnostic.Source, ["line"] = diagnostic.Line, ["column"] = diagnostic.Column,
                ["message"] = diagnostic.Message,
                ["frames"] = new JsonArray((error is ScriptExecutionException runtimeError ? runtimeError.Frames : [])
                    .Select(frame => (JsonNode)new JsonObject
                    {
                        ["name"] = frame.Name, ["source"] = frame.Source, ["line"] = frame.Line,
                        ["column"] = frame.Column
                    }).ToArray())
            };
        }

        private async Task<JsonNode?> HandleAsync(string method, JsonNode? args)
        {
            if (method == "initialize")
                return new JsonObject
                {
                    ["capabilities"] = new JsonObject
                    {
                        ["textDocumentSync"] = 1,
                        ["completionProvider"] = new JsonObject { ["triggerCharacters"] = new JsonArray(".") },
                        ["signatureHelpProvider"] = new JsonObject { ["triggerCharacters"] = new JsonArray("(", ",") },
                        ["hoverProvider"] = true, ["definitionProvider"] = true, ["documentSymbolProvider"] = true
                    }
                };
            if (method == "shutdown") return null;
            if (method == "rgscript/executableLines")
                return new JsonArray(PresetLanguage.ExecutableLines(args!["text"]!.GetValue<string>())
                    .Select(n => (JsonNode)JsonValue.Create(n)).ToArray());
            if (method == "rgscript/command")
            {
                if (command is null) throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                return await command(args!["arguments"]!.AsArray().Select(v => v!.GetValue<string>()).ToArray());
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
            if (method == "textDocument/completion")
                return new JsonArray(model.Complete(offset).Select(c => (JsonNode)new JsonObject
                    {
                        ["label"] = c.Label, ["insertText"] = c.InsertText, ["detail"] = c.Detail,
                        ["kind"] = c.Kind switch
                        {
                            "method" => 2, "function" => 3, "variable" => 6, "property" => 10, "enumMember" => 20,
                            "class" => 7, "module" => 9, "enum" => 13, _ => 14
                        }
                    })
                    .ToArray());
            if (method == "textDocument/documentSymbol")
                return new JsonArray(model.Symbols.Select(s => (JsonNode)new JsonObject
                {
                    ["name"] = s.Name, ["kind"] = s.Kind == "fn" ? 12 : s.Kind is "let" or "const" ? 13 : 5,
                    ["range"] = Range(s.Line, s.Column, s.Name.Length),
                    ["selectionRange"] = Range(s.Line, s.Column, s.Name.Length)
                }).ToArray());
            if (method == "textDocument/signatureHelp")
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
                                .Select(p => (JsonNode)new JsonObject { ["label"] = p }).ToArray())
                        }),
                        ["activeSignature"] = 0, ["activeParameter"] = signature.ActiveParameter
                    };
            }

            var word = PresetLanguage.WordAt(source, offset);
            if (word is null) return null;
            if (method == "textDocument/hover")
                return new JsonObject
                {
                    ["contents"] = new JsonObject
                        { ["kind"] = "plaintext", ["value"] = model.Hint(offset) ?? word.Text },
                    ["range"] = Range(word.Line, word.Column, word.Length)
                };
            if (method == "textDocument/definition")
            {
                var symbol = model.Definition(offset);
                return symbol is null
                    ? null
                    : new JsonObject
                    {
                        ["uri"] = symbol.File is { } file ? new Uri(file).AbsoluteUri : uri,
                        ["range"] = Range(symbol.Line, symbol.Column, symbol.Name.Length)
                    };
            }

            return null;
        }

        private static async Task SendAsync(Stream output, JsonNode message, CancellationToken token)
        {
            using var buffer = new MemoryStream();
            using (var writer = new System.Text.Json.Utf8JsonWriter(buffer)) message.WriteTo(writer);
            var bytes = buffer.ToArray();
            await output.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n"), token);
            await output.WriteAsync(bytes, token);
            await output.FlushAsync(token);
        }
    }
}

namespace RitsukageGif.Scripting
{
    public static class ClipboardScriptLibrary
    {
        public static void Register(ScriptBindingRegistry bindings, IClipboardAutomationService? service = null,
            string? baseDirectory = null)
        {
            ScriptServiceErrors.Register(bindings);
            var root = Path.GetFullPath(baseDirectory ?? Environment.CurrentDirectory);
            var table = bindings.RegisterTable("clipboard");
            table.Function("isAvailable", (_, token) => Execute(async () =>
                await Required(service).IsAvailableAsync(token), token), [], "Result<boolean,ServiceError>");
            table.Function("readText", (_, token) => Execute(async () =>
                await Required(service).ReadTextAsync(token), token), [], "Result<string?,ServiceError>");
            table.Function("readHtml", (_, token) => Execute(async () =>
                await Required(service).ReadHtmlAsync(token), token), [], "Result<string?,ServiceError>");
            table.Function("readFiles", (_, token) => Execute(async () =>
                    (await Required(service).ReadFilesAsync(token)).Cast<object?>().ToList(), token), [],
                "Result<string[],ServiceError>");
            table.Function("readImage", (_, token) => Execute(async () =>
                {
                    var png = await Required(service).ReadImageAsync(token);
                    return png is null ? null : new ScriptBytes(png);
                }, token), [], "Result<Bytes?,ServiceError>",
                "Returns PNG image bytes, preserving an existing PNG representation; other images use their first frame.");
            table.Function("writeText", (args, token) => Execute(async () =>
            {
                await Required(service).WriteTextAsync((string)args[0]!, token);
                return null;
            }, token), [new("text", "string")], "Result<void,ServiceError>");
            table.Function("writeHtml", (args, token) => Execute(async () =>
            {
                await Required(service).WriteHtmlAsync((string)args[0]!, args[1] as string, token);
                return null;
            }, token), [new("html", "string"), new("text", "string?", true)], "Result<void,ServiceError>");
            table.Function("writeFiles", (args, token) => Execute(async () =>
            {
                var files = ((IReadOnlyList<object?>)args[0]!).Select(value =>
                        Path.GetFullPath(value as string ?? throw new ArgumentException(UiText.Get("ScriptTypeError")),
                            root))
                    .ToArray();
                await Required(service).WriteFilesAsync(files, token);
                return null;
            }, token), [new("paths", "string[]")], "Result<void,ServiceError>");
            table.Function("writeImage", (args, token) => Execute(async () =>
                {
                    await Required(service).WriteImageAsync(((ScriptBytes)args[0]!).Data, token);
                    return null;
                }, token), [new("png", "Bytes")], "Result<void,ServiceError>",
                "Writes PNG bytes with the platform bitmap fallback. Does not paste or activate another window.");
            table.Function("clear", (_, token) => Execute(async () =>
            {
                await Required(service).ClearAsync(token);
                return null;
            }, token), [], "Result<void,ServiceError>");
        }

        private static IClipboardAutomationService Required(IClipboardAutomationService? service)
        {
            return service ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
        }

        private static async ValueTask<object?> Execute(Func<Task<object?>> action, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                return new ScriptResult(true, await action());
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                token.ThrowIfCancellationRequested();
                return new ScriptResult(false, ScriptServiceErrors.FromException(error));
            }
        }
    }
}

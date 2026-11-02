namespace RitsukageGif.Scripting
{
    public static partial class RecognitionScriptLibrary
    {
        private static void RegisterFormulaDocument(ScriptTableBinding formula, IFormulaOutputService? output,
            IFormulaDocumentService? documents)
        {
            formula.Function("documentCapabilities", (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                var available = output is not null && documents is not null;
                return ValueTask.FromResult<object?>(new ScriptResult(true,
                    StructuredCapabilities(new(available, available ? null : UiText.Get("EditorNoRuntime")))));
            }, [], "Result<RecognitionProviderCapabilities,ServiceError>");
            formula.Function("document", async (args, token) =>
            {
                try
                {
                    var converter = output ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                    var encoder = documents ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                    var sources = FormulaSources(args[0]);
                    var mathMl = new string[sources.Length];
                    for (var index = 0; index < sources.Length; index++)
                    {
                        var converted = await converter.ConvertAsync([sources[index]], FormulaTextFormat.MathMl, token);
                        token.ThrowIfCancellationRequested();
                        if (converted.ValidationError is { } error) throw new InvalidOperationException(error);
                        mathMl[index] = converted.Text ??
                                        throw new InvalidDataException("The formula converter returned no MathML.");
                    }

                    var bytes = await encoder.EncodeDocxAsync(mathMl, token);
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(true, new ScriptBytes(bytes));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(error));
                }
            }, [new("sources", "string|string[]")], "Result<Bytes,ServiceError>");
        }
    }
}

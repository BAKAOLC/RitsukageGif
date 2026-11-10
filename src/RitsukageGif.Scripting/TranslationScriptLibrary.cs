namespace RitsukageGif.Scripting
{
    public static class TranslationScriptLibrary
    {
        public static void Register(ScriptBindingRegistry bindings, ITextTranslationService? service = null)
        {
            ScriptServiceErrors.Register(bindings);
            bindings.RegisterObjectSchema("TranslationOptions",
            [
                new("sourceLanguage", "string", true), new("targetLanguage", "string", true),
                new("timeoutSeconds", "number", true),
            ]);
            bindings.RegisterObjectSchema("TranslationCapabilities",
            [
                new("available", "boolean"), new("configured", "boolean"), new("provider", "string"),
                new("sourceLanguage", "string"), new("targetLanguage", "string"),
                new("maximumTextLength", "number"), new("failureReason", "string?"),
            ]);
            bindings.RegisterObjectSchema("TranslationResult",
                [new("text", "string"), new("detectedSourceLanguage", "string?")]);
            var table = bindings.RegisterTable("translation");
            table.Function("capabilities", async (_, token) =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    var value = service is null
                        ? new(false, false, "", "auto", "", 100000, UiText.Get("EditorNoRuntime"))
                        : await service.GetCapabilitiesAsync(token);
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(true, new Dictionary<string, object?>
                    {
                        ["available"] = value.Available, ["configured"] = value.Configured,
                        ["provider"] = value.Provider, ["sourceLanguage"] = value.SourceLanguage,
                        ["targetLanguage"] = value.TargetLanguage,
                        ["maximumTextLength"] = (double)value.MaximumTextLength,
                        ["failureReason"] = value.FailureReason,
                    });
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(error));
                }
            }, [], "Result<TranslationCapabilities,ServiceError>");
            table.Function("translate", async (args, token) =>
                {
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        var host = service ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                        var value = await host.TranslateAsync((string)args[0]!, ReadOptions(args[1]), token);
                        token.ThrowIfCancellationRequested();
                        return new ScriptResult(true, new Dictionary<string, object?>
                        {
                            ["text"] = value.Text, ["detectedSourceLanguage"] = value.DetectedSourceLanguage,
                        });
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        token.ThrowIfCancellationRequested();
                        return new ScriptResult(false, error is TimeoutException
                            ? ScriptServiceErrors.Create("timedOut", error.Message)
                            : ScriptServiceErrors.FromException(error));
                    }
                }, [new("text", "string"), new("options", "TranslationOptions", true)],
                "Result<TranslationResult,ServiceError>");
        }

        private static TextTranslationOptions ReadOptions(object? value)
        {
            if (value is not Dictionary<string, object?> fields) return new();
            int? timeout = null;
            if (fields.TryGetValue("timeoutSeconds", out var raw))
            {
                var number = ScriptEngine.Number(raw);
                if (!double.IsFinite(number) || !number.Equals(Math.Truncate(number)) || number is < 1 or > 300)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "Translation timeout must be an integer between 1 and 300 seconds.");
                timeout = (int)number;
            }

            return new()
            {
                SourceLanguage = fields.GetValueOrDefault("sourceLanguage") as string,
                TargetLanguage = fields.GetValueOrDefault("targetLanguage") as string,
                TimeoutSeconds = timeout,
            };
        }
    }
}

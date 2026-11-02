using System.Text;

namespace RitsukageGif.Scripting
{
    public static partial class RecognitionScriptLibrary
    {
        private static void RegisterFormulaOutput(ScriptBindingRegistry bindings, ScriptTableBinding formula,
            IFormulaOutputService? service)
        {
            bindings.RegisterEnum("FormulaLatexFormat",
                Enum.GetValues<FormulaLatexFormat>().ToDictionary(value => value.ToString(), value => value));
            formula.Function("format", ScriptEngine.Native(args =>
                    FormulaLatexFormatter.FormatMany(FormulaSources(args[0]), (FormulaLatexFormat)args[1]!)),
                [
                    new("sources", "string|string[]"),
                    new("format", "FormulaLatexFormat", true, FormulaLatexFormat.DisplayDollar),
                ], "string");
            formula.Function("unwrap", ScriptEngine.Native(args => FormulaLatexFormatter.Unwrap((string)args[0]!)),
                [new("source", "string")], "string");
            bindings.RegisterEnum("FormulaImageFormat",
                Enum.GetValues<FormulaImageFormat>().ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("FormulaTextFormat",
                Enum.GetValues<FormulaTextFormat>().ToDictionary(value => value.ToString(), value => value));
            formula.Function("outputCapabilities", (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult<object?>(new ScriptResult(true,
                    StructuredCapabilities(new(service is not null,
                        service is null ? UiText.Get("EditorNoRuntime") : null))));
            }, [], "Result<RecognitionProviderCapabilities,ServiceError>");
            formula.Function("render", async (args, token) =>
            {
                try
                {
                    var output = service ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                    var color = (double)args[3]!;
                    if (!double.IsInteger(color) || color is < 0 or > uint.MaxValue)
                        throw new ArgumentOutOfRangeException(nameof(args));
                    var result = await output.RenderAsync(FormulaSources(args[0]), (FormulaImageFormat)args[1]!,
                        (float)(double)args[2]!, (uint)color, token);
                    token.ThrowIfCancellationRequested();
                    if (result.ValidationError is { } error) throw new InvalidOperationException(error);
                    return new ScriptResult(true, new ScriptBytes(result.Png ?? Encoding.UTF8.GetBytes(result.Svg!)));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(error));
                }
            }, [
                new("sources", "string|string[]"), new("format", "FormulaImageFormat", true, FormulaImageFormat.Png),
                new("fontSize", "number", true, 28d), new("textColor", "number", true, 4278190080d),
            ], "Result<Bytes,ServiceError>");
            formula.Function("convert", async (args, token) =>
                {
                    try
                    {
                        var output = service ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                        var result = await output.ConvertAsync(FormulaSources(args[0]), (FormulaTextFormat)args[1]!,
                            token);
                        token.ThrowIfCancellationRequested();
                        if (result.ValidationError is { } error) throw new InvalidOperationException(error);
                        return new ScriptResult(true, result.Text);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        token.ThrowIfCancellationRequested();
                        return new ScriptResult(false, ScriptServiceErrors.FromException(error));
                    }
                },
                [new("sources", "string|string[]"), new("format", "FormulaTextFormat", true, FormulaTextFormat.Latex)],
                "Result<string,ServiceError>");
        }

        private static string[] FormulaSources(object? value)
        {
            return value is string text
                ? [text]
                : [.. ((List<object?>)value!).Select(item => (string)item!)];
        }
    }
}

namespace RitsukageGif.Scripting
{
    public static partial class RecognitionScriptLibrary
    {
        private static void RegisterStructuredRecognition(ScriptBindingRegistry bindings,
            ITableRecognitionService? tables, IFormulaRecognitionService? formulas, string root,
            IFormulaOutputService? formulaOutput, ITableOutputService? tableOutput,
            IFormulaDocumentService? formulaDocument, ITableDataService? tableData, ITableEditService? tableEdit)
        {
            bindings.RegisterObjectSchema("RecognitionProviderCapabilities",
                [new("available", "boolean"), new("unavailableReason", "string?")]);
            bindings.RegisterObjectSchema("TableCell",
            [
                new("row", "number"), new("column", "number"), new("rowSpan", "number"),
                new("columnSpan", "number"), new("text", "string"), new("bounds", "OcrBounds"),
                new("value", "TableCellValue", true),
            ]);
            bindings.RegisterObjectSchema("TableRecognitionResult",
            [
                new("rows", "number"), new("columns", "number"), new("width", "number"),
                new("height", "number"), new("cells", "TableCell[]"), new("structureConfidence", "number?"),
            ]);
            bindings.RegisterObjectSchema("RecognizedFormula",
                [new("latex", "string"), new("bounds", "OcrBounds"), new("detectionConfidence", "number?")]);
            bindings.RegisterObjectSchema("FormulaRecognitionResult",
                [new("width", "number"), new("height", "number"), new("formulas", "RecognizedFormula[]")]);
            var table = bindings.RegisterTable("table");
            RegisterTableOutput(bindings, table, tableOutput, tableData, tableEdit, root);
            table.Function("capabilities", async (_, token) =>
            {
                try
                {
                    var capabilities = tables is null
                        ? new(false, UiText.Get("EditorNoRuntime"))
                        : await tables.GetCapabilitiesAsync(token);
                    return new ScriptResult(true, StructuredCapabilities(capabilities));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(error));
                }
            }, [], "Result<RecognitionProviderCapabilities,ServiceError>");
            table.Function("recognize", async (args, token) =>
            {
                try
                {
                    var service = tables ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                    var image = await ReadImage(args[0], root, token);
                    var result = await service.RecognizeAsync(image, token);
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(true, TableResult(result));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(error, args[0] as string));
                }
            }, [new("image", "string|Bytes")], "Result<TableRecognitionResult,ServiceError>");
            var formula = bindings.RegisterTable("formula");
            RegisterFormulaOutput(bindings, formula, formulaOutput);
            RegisterFormulaDocument(formula, formulaOutput, formulaDocument);
            formula.Function("capabilities", async (_, token) =>
            {
                try
                {
                    var capabilities = formulas is null
                        ? new(false, UiText.Get("EditorNoRuntime"))
                        : await formulas.GetCapabilitiesAsync(token);
                    return new ScriptResult(true, StructuredCapabilities(capabilities));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(error));
                }
            }, [], "Result<RecognitionProviderCapabilities,ServiceError>");
            formula.Function("recognize", async (args, token) =>
            {
                try
                {
                    var service = formulas ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                    var image = await ReadImage(args[0], root, token);
                    var result = await service.RecognizeAsync(image, token);
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(true, new Dictionary<string, object?>
                    {
                        ["width"] = (double)result.Width, ["height"] = (double)result.Height,
                        ["formulas"] = result.Formulas.Select(object? (recognizedFormula) =>
                            new Dictionary<string, object?>
                            {
                                ["latex"] = recognizedFormula.Latex, ["bounds"] = Bounds(recognizedFormula.Bounds),
                                ["detectionConfidence"] =
                                    recognizedFormula.DetectionConfidence is { } confidence ? (double)confidence : null,
                            }).ToList(),
                    });
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(error, args[0] as string));
                }
            }, [new("image", "string|Bytes")], "Result<FormulaRecognitionResult,ServiceError>");
        }

        private static Dictionary<string, object?> StructuredCapabilities(RecognitionProviderCapabilities value)
        {
            return new() { ["available"] = value.Available, ["unavailableReason"] = value.UnavailableReason };
        }
    }
}

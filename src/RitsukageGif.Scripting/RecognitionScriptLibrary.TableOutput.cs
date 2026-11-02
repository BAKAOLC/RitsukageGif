using System.Globalization;

namespace RitsukageGif.Scripting
{
    public static partial class RecognitionScriptLibrary
    {
        private static void RegisterTableOutput(ScriptBindingRegistry bindings, ScriptTableBinding table,
            ITableOutputService? service, ITableDataService? dataService, ITableEditService? editService, string root)
        {
            bindings.RegisterEnum("TableOutputFormat",
                Enum.GetValues<TableOutputFormat>().ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("TableCellValueKind",
                Enum.GetValues<TableCellValueKind>().ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterObjectSchema("TableCellValue",
            [
                new("kind", "TableCellValueKind"), new("boolean", "boolean", true),
                new("integer", "string", true), new("number", "number", true),
            ]);
            bindings.RegisterObjectSchema("TableExportCell",
            [
                new("row", "number"), new("column", "number"), new("text", "string|number|boolean"),
                new("rowSpan", "number", true), new("columnSpan", "number", true),
                new("value", "TableCellValue", true),
            ]);
            bindings.RegisterObjectSchema("TableExportData",
                [new("rows", "number"), new("columns", "number"), new("cells", "TableExportCell[]")]);
            table.Function("export", async (args, token) =>
            {
                try
                {
                    var output = service ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                    var result = await output.EncodeAsync(TableData(args[0]), (TableOutputFormat)args[1]!, token);
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(true, new ScriptBytes(result));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(error));
                }
            }, [
                new("data", "TableRecognitionResult|TableExportData"),
                new("format", "TableOutputFormat", true, TableOutputFormat.Xlsx),
            ], "Result<Bytes,ServiceError>");
            table.Function("read", async (args, token) =>
            {
                if (dataService is null)
                    return new ScriptResult(false,
                        ScriptServiceErrors.Create("unavailable", UiText.Get("EditorNoRuntime")));
                try
                {
                    var bytes = await ReadImage(args[0], root, token);
                    var result = await dataService.ReadXlsxAsync(bytes, token);
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(true, TableResult(result));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(error, args[0] as string));
                }
            }, [new("source", "string|Bytes")], "Result<TableRecognitionResult,ServiceError>");
            table.Function("edit", async (args, token) =>
            {
                if (editService is null)
                    return new ScriptResult(false,
                        ScriptServiceErrors.Create("unavailable", UiText.Get("EditorNoRuntime")));
                try
                {
                    var result = await editService.EditAsync(TableData(args[0]), token);
                    token.ThrowIfCancellationRequested();
                    return result is null
                        ? new ScriptResult(false,
                            ScriptServiceErrors.Create("cancelled", UiText.Get("RecognitionCancelled")))
                        : new ScriptResult(true, TableResult(result));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return new ScriptResult(false, ScriptServiceErrors.FromException(error));
                }
            }, [new("data", "TableRecognitionResult|TableExportData")], "Result<TableRecognitionResult,ServiceError>");
        }

        private static TableRecognitionResult TableData(object? data)
        {
            var fields = (Dictionary<string, object?>)data!;
            var cells = ((List<object?>)fields["cells"]!).Select(item =>
            {
                var cell = (Dictionary<string, object?>)item!;
                var source = cell["text"];
                var value = source switch
                {
                    bool boolean => new TableCellValue(TableCellValueKind.Boolean, boolean),
                    double number => new TableCellValue(TableCellValueKind.Number, Number: number),
                    _ => default,
                };
                if (cell.TryGetValue("value", out var metadata))
                {
                    var typed = (Dictionary<string, object?>)metadata!;
                    value = (TableCellValueKind)typed["kind"]! switch
                    {
                        TableCellValueKind.Text => default,
                        TableCellValueKind.Boolean => new(TableCellValueKind.Boolean, (bool)typed["boolean"]!),
                        TableCellValueKind.Int64 => new(TableCellValueKind.Int64,
                            Integer: long.Parse((string)typed["integer"]!, NumberStyles.AllowLeadingSign,
                                CultureInfo.InvariantCulture)),
                        TableCellValueKind.Number => new(TableCellValueKind.Number, Number: (double)typed["number"]!),
                        _ => throw new ArgumentException("The table cell value kind is invalid."),
                    };
                }

                var text = value.Kind == TableCellValueKind.Text ? (string)source! : value.ToDisplayText();
                return new TableCell(TableInteger(cell, "row"), TableInteger(cell, "column"),
                        TableInteger(cell, "rowSpan", 1), TableInteger(cell, "columnSpan", 1), text, default)
                    { Value = value };
            }).ToArray();
            return new(TableInteger(fields, "rows"), TableInteger(fields, "columns"),
                TableInteger(fields, "width", 0), TableInteger(fields, "height", 0), cells);
        }

        private static Dictionary<string, object?> TableResult(TableRecognitionResult result)
        {
            return new()
            {
                ["rows"] = (double)result.Rows, ["columns"] = (double)result.Columns,
                ["width"] = (double)result.Width, ["height"] = (double)result.Height,
                ["structureConfidence"] = result.StructureConfidence is { } score ? (double)score : null,
                ["cells"] = result.Cells.Select(object? (cell) => TableResultCell(cell)).ToList(),
            };
        }

        private static Dictionary<string, object?> TableResultCell(TableCell cell)
        {
            Dictionary<string, object?> result = new()
            {
                ["row"] = (double)cell.Row, ["column"] = (double)cell.Column,
                ["rowSpan"] = (double)cell.RowSpan, ["columnSpan"] = (double)cell.ColumnSpan,
                ["text"] = cell.Text, ["bounds"] = Bounds(cell.Bounds),
            };
            if (cell.Value.Kind == TableCellValueKind.Text) return result;
            result["value"] = new Dictionary<string, object?>
            {
                ["kind"] = cell.Value.Kind, ["boolean"] = cell.Value.Boolean,
                ["integer"] = cell.Value.Integer.ToString(CultureInfo.InvariantCulture),
                ["number"] = cell.Value.Number,
            };
            return result;
        }

        private static int TableInteger(Dictionary<string, object?> fields, string key, int? fallback = null)
        {
            if (!fields.TryGetValue(key, out var value) && fallback is { } defaultValue) return defaultValue;
            if (value is not double number || !double.IsInteger(number) || number is < int.MinValue or > int.MaxValue)
                throw new ArgumentException("The table cell indices and dimensions must be integers.");
            return (int)number;
        }
    }
}

namespace RitsukageGif.Scripting
{
    public static class DocumentScriptLibrary
    {
        public static void Register(ScriptBindingRegistry bindings, IDocumentOutputService? service = null,
            string? baseDirectory = null)
        {
            var root = Path.GetFullPath(baseDirectory ?? Environment.CurrentDirectory);
            ScriptServiceErrors.Register(bindings);
            bindings.RegisterEnum("DocumentPaperSize",
                Enum.GetValues<DocumentPaperSize>().ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("DocumentOrientation",
                Enum.GetValues<DocumentOrientation>().ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("DocumentPageMode",
                Enum.GetValues<DocumentPageMode>().ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("DocumentPaginationDirection",
                Enum.GetValues<DocumentPaginationDirection>().ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterObjectSchema("DocumentMargins",
            [
                new("left", "number", true), new("top", "number", true), new("right", "number", true),
                new("bottom", "number", true),
            ]);
            bindings.RegisterObjectSchema("DocumentOutputOptions", [
                new("paperSize", "DocumentPaperSize", true), new("orientation", "DocumentOrientation", true),
                new("pageMode", "DocumentPageMode", true),
                new("paginationDirection", "DocumentPaginationDirection", true),
                new("widthMillimeters", "number", true), new("heightMillimeters", "number", true),
                new("margins", "DocumentMargins", true), new("dpi", "number", true), new("background", "string", true),
                new("lossless", "boolean", true), new("quality", "number", true), new("overwrite", "boolean", true),
            ]);
            bindings.RegisterObjectSchema("DocumentInfo",
                [new("path", "string"), new("bytes", "number"), new("pages", "number")]);
            bindings.RegisterTable("document").Function("save", async (args, token) =>
                {
                    var output = (string)args[1]!;
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        var files = ((List<object?>)args[0]!).Select(value => Path.GetFullPath((string)value!, root))
                            .ToArray();
                        var fields = args[2] as Dictionary<string, object?> ?? [];
                        var margins = fields.GetValueOrDefault("margins") as Dictionary<string, object?> ?? [];
                        var quality = Number(fields, "quality", 90);
                        if (!double.IsInteger(quality) || quality is < 0 or > 100)
                            throw new ArgumentOutOfRangeException(nameof(args));
                        var options = new DocumentOutputOptions
                        {
                            PaperSize = fields.GetValueOrDefault("paperSize") as DocumentPaperSize? ??
                                        DocumentPaperSize.Original,
                            Orientation = fields.GetValueOrDefault("orientation") as DocumentOrientation? ??
                                          DocumentOrientation.Automatic,
                            PageMode =
                                fields.GetValueOrDefault("pageMode") as DocumentPageMode? ?? DocumentPageMode.Fit,
                            PaginationDirection =
                                fields.GetValueOrDefault("paginationDirection") as DocumentPaginationDirection? ??
                                DocumentPaginationDirection.Vertical,
                            WidthMillimeters = Number(fields, "widthMillimeters", 210),
                            HeightMillimeters = Number(fields, "heightMillimeters", 297),
                            Margins = new()
                            {
                                Left = Number(margins, "left", 0), Top = Number(margins, "top", 0),
                                Right = Number(margins, "right", 0), Bottom = Number(margins, "bottom", 0),
                            },
                            Dpi = Number(fields, "dpi", 96),
                            Background = fields.GetValueOrDefault("background") as string ?? "#ffffff",
                            Lossless = fields.GetValueOrDefault("lossless") is not false, Quality = (int)quality,
                            Overwrite = fields.GetValueOrDefault("overwrite") is true,
                        };
                        var host = service ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
                        var info = await host.SaveAsync(files, Path.GetFullPath(output, root), options, token);
                        return new ScriptResult(true,
                            new Dictionary<string, object?>
                            {
                                ["path"] = info.Path, ["bytes"] = (double)info.Bytes, ["pages"] = (double)info.Pages,
                            });
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        token.ThrowIfCancellationRequested();
                        return new ScriptResult(false, ScriptServiceErrors.FromException(error, output));
                    }
                }, [new("images", "string[]"), new("output", "string"), new("options", "DocumentOutputOptions", true)],
                "Result<DocumentInfo,ServiceError>");
        }

        private static double Number(Dictionary<string, object?> fields, string name, double fallback)
        {
            return fields.GetValueOrDefault(name) is { } value ? ScriptEngine.Number(value) : fallback;
        }
    }
}

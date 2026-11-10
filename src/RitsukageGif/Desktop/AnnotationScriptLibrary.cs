using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal static partial class AnnotationScriptLibrary
    {
        internal static void Register(ScriptBindingRegistry bindings, AnnotationScriptService? service = null)
        {
            ScriptServiceErrors.Register(bindings);
            RegisterProperties(bindings);
            bindings.RegisterEnum("AnnotationTool", Enum.GetValues<ScreenshotTool>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterEnum("StaticImageFormat", Enum.GetValues<StaticImageFormat>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterObjectSchema("AnnotationPoint", [new("x", "number"), new("y", "number")]);
            bindings.RegisterObjectSchema("AnnotationStyle",
            [
                new("color", "string", true), new("thickness", "number", true), new("fontSize", "number", true),
                new("mosaicSize", "number", true), new("text", "string", true),
                new("properties", "AnnotationProperties", true), new("image", "string|Bytes", true),
                new("magnifierOffset", "AnnotationPoint", true),
            ]);
            bindings.RegisterEnum("HdrImageFormat", Enum.GetValues<HdrImageFormat>()
                .ToDictionary(value => value.ToString(), value => value));
            bindings.RegisterObjectSchema("AnnotationHdrExportOptions",
                [new("format", "HdrImageFormat", true), new("overwrite", "boolean", true)]);
            bindings.RegisterObjectSchema("AnnotationExportOptions",
            [
                new("format", "StaticImageFormat", true), new("quality", "number", true),
                new("background", "string", true), new("overwrite", "boolean", true),
            ]);
            var table = bindings.RegisterTable("annotation");
            table.Function("open", (args, token) => Try(async () => await Host().OpenAsync(args[0]!, token), token),
                [new("image", "string|Bytes")], "Result<AnnotationCanvas,ServiceError>");
            table.Function("create", (args, token) => Try(async () => await Host().CreateAsync(
                    Integer(args[0]), Integer(args[1]), (string)args[2]!, token), token),
                [new("width", "number"), new("height", "number"), new("background", "string", true, "transparent")],
                "Result<AnnotationCanvas,ServiceError>");
            table.Function("capture", (_, token) => Try(async () => await Host().CaptureAsync(token), token),
                [], "Result<AnnotationCanvas,ServiceError>");
            var canvas = bindings.RegisterType<AnnotationScriptCanvas>("AnnotationCanvas");
            canvas.Property("width", "number", value => value.Width);
            canvas.Property("height", "number", value => value.Height);
            canvas.Property("count", "number", value => value.Count);
            canvas.Property("canUndo", "boolean", value => value.CanUndo);
            canvas.Property("canRedo", "boolean", value => value.CanRedo);
            canvas.Property("isClosed", "boolean", value => value.IsClosed);
            ScriptParameter[] add =
            [
                new("tool", "AnnotationTool"), new("points", "AnnotationPoint[]"),
                new("style", "AnnotationStyle", true),
            ];
            canvas.Method("add", (value, args, token) => Add(value, (ScreenshotTool)args[0]!,
                Points(args[1]), args[2], token), add, "Result<number,ServiceError>");
            canvas.Method("replace", (value, args, token) => Add(value, (ScreenshotTool)args[1]!,
                    Points(args[2]), args[3], token, Integer(args[0])), [new("index", "number"), .. add],
                "Result<number,ServiceError>");
            foreach (var (name, tool) in new[]
                     {
                         ("rect", ScreenshotTool.Rectangle), ("ellipse", ScreenshotTool.Ellipse),
                         ("mosaic", ScreenshotTool.Mosaic), ("blur", ScreenshotTool.Blur),
                         ("spotlight", ScreenshotTool.Spotlight), ("magnifier", ScreenshotTool.Magnifier),
                         ("smartErase", ScreenshotTool.SmartErase),
                     })
                canvas.Method(name, (value, args, token) => Try(async () =>
                {
                    var x = Number(args[0]);
                    var y = Number(args[1]);
                    var width = Number(args[2]);
                    var height = Number(args[3]);
                    if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(args));
                    return await AddCore(value, tool, [new(x, y), new(x + width, y + height)], args[4], token,
                        pixelPath: tool is ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.SmartErase
                            ? AnnotationPixelPath.Rectangle
                            : null);
                }, token), RectangleParameters(), "Result<number,ServiceError>");
            foreach (var (name, tool) in new[]
                     {
                         ("line", ScreenshotTool.Line), ("arrow", ScreenshotTool.Arrow),
                     })
                canvas.Method(name, (value, args, token) => Add(value, tool,
                        [new(Number(args[0]), Number(args[1])), new(Number(args[2]), Number(args[3]))], args[4], token),
                    [
                        new("x1", "number"), new("y1", "number"), new("x2", "number"), new("y2", "number"),
                        new("style", "AnnotationStyle", true),
                    ], "Result<number,ServiceError>");
            foreach (var (name, tool) in new[]
                     {
                         ("polyline", ScreenshotTool.Polyline), ("brush", ScreenshotTool.Brush),
                         ("highlight", ScreenshotTool.Highlight), ("erase", ScreenshotTool.Eraser),
                     })
                canvas.Method(name, (value, args, token) => Add(value, tool, Points(args[0]), args[1], token),
                    [new("points", "AnnotationPoint[]"), new("style", "AnnotationStyle", true)],
                    "Result<number,ServiceError>");
            canvas.Method("text", (value, args, token) => Add(value, ScreenshotTool.Text,
                    [new(Number(args[1]), Number(args[2]))], args[3], token, text: (string)args[0]!),
                [
                    new("text", "string"), new("x", "number"), new("y", "number"),
                    new("style", "AnnotationStyle", true),
                ], "Result<number,ServiceError>");
            canvas.Method("number", (value, args, token) => Add(value, ScreenshotTool.Number,
                    [new(Number(args[1]), Number(args[2]))], args[3], token, number: Integer(args[0])),
                [
                    new("number", "number"), new("x", "number"), new("y", "number"),
                    new("style", "AnnotationStyle", true),
                ], "Result<number,ServiceError>");
            canvas.Method("watermark", (value, args, token) => Add(value, ScreenshotTool.Watermark,
                    [new(Number(args[1]), Number(args[2]))], WithImage(args[3], args[0]), token),
                [
                    new("image", "string|Bytes"), new("x", "number"), new("y", "number"),
                    new("style", "AnnotationStyle", true),
                ], "Result<number,ServiceError>");
            canvas.Method("eraseStrokes", (value, args, token) => Try(() =>
                    value.EraseStrokesAsync(Points(args[0]), token), token),
                [new("points", "AnnotationPoint[]")], "Result<number,ServiceError>");
            canvas.Method("mosaicCandidates", (value, args, token) => Try(async () =>
                {
                    var candidates = ReadCandidates(args[0]);
                    var selected = candidates.Where(candidate => candidate.IsSensitive).ToArray();
                    if (selected.Length == 0) return null;
                    var fields = Fields(args[1]);
                    var record = Record(ScreenshotTool.Mosaic,
                        [new(0, 0), new(value.Width, value.Height)], fields);
                    if (record.Properties.MosaicMode == AnnotationMosaicMode.SmartErase)
                        throw new ArgumentException("Automatic mosaic does not support smart erasure.");
                    record = record with
                    {
                        Tool = record.Properties.MosaicMode == AnnotationMosaicMode.Blur
                            ? ScreenshotTool.Blur
                            : ScreenshotTool.Mosaic,
                        AutoMosaicId = Guid.NewGuid(), AutoMosaicCandidates = [.. candidates],
                        AutoMosaicCandidateBounds = new(0, 0, value.Width, value.Height),
                        AutoMosaicRegions =
                        [
                            .. selected.Select(candidate => new AnnotationDocumentRegion(
                                (double)candidate.Bounds.X / value.Width, (double)candidate.Bounds.Y / value.Height,
                                (double)candidate.Bounds.Width / value.Width,
                                (double)candidate.Bounds.Height / value.Height)),
                        ],
                    };
                    return await value.AddAsync(record, null, token);
                }, token), [new("candidates", "PrivacyCandidate[]"), new("style", "AnnotationStyle", true)],
                "Result<number?,ServiceError>");
            canvas.Method("move", (value, args, token) => Try(() => value.MoveAsync(Integer(args[0]),
                    Number(args[1]), Number(args[2]), token), token),
                [new("index", "number"), new("dx", "number"), new("dy", "number")], "Result<number,ServiceError>");
            canvas.Method("remove",
                (value, args, token) => Try(() => value.EditAsync("remove", Integer(args[0]), token), token),
                [new("index", "number")], "Result<void,ServiceError>");
            foreach (var name in new[] { "clear", "undo", "redo" })
                canvas.Method(name, (value, _, token) => Try(() => value.EditAsync(name, 0, token), token),
                    [], "Result<void,ServiceError>");
            canvas.Method("image", (value, _, token) => Try(() => value.ImageAsync(token), token),
                [], "Result<Bytes,ServiceError>");
            canvas.Method("export", (value, args, token) => Try(() => value.ExportAsync((string)args[0]!,
                ExportOptions(args[1]), token), token), [
                new("path", "string"),
                new("options", "AnnotationExportOptions", true),
            ], "Result<MediaInfo,ServiceError>");
            canvas.Method("imageHdr", (value, _, token) => Try(() => value.ImageHdrAsync(token), token),
                [], "Result<Bytes,ServiceError>");
            canvas.Method("exportHdr", (value, args, token) => Try(() => value.ExportHdrAsync((string)args[0]!,
                    new()
                    {
                        Format = Fields(args[1]).GetValueOrDefault("format") as HdrImageFormat?,
                        Overwrite = Fields(args[1]).GetValueOrDefault("overwrite") is true,
                    }, token), token), [new("path", "string"), new("options", "AnnotationHdrExportOptions", true)],
                "Result<MediaInfo,ServiceError>");
            canvas.Method("close", (value, _, token) => Try(async () =>
            {
                await value.DisposeAsync();
                return null;
            }, token), [], "Result<void,ServiceError>");
            return;

            AnnotationScriptService Host()
            {
                return service ?? throw new InvalidOperationException(UiText.Get("EditorNoRuntime"));
            }
        }

        private static ScriptParameter[] RectangleParameters()
        {
            return
            [
                new("x", "number"), new("y", "number"), new("width", "number"), new("height", "number"),
                new("style", "AnnotationStyle", true),
            ];
        }

        private static ValueTask<object?> Add(AnnotationScriptCanvas canvas, ScreenshotTool tool,
            AnnotationDocumentPoint[] points, object? style, CancellationToken token, int? replaceIndex = null,
            string? text = null, int? number = null)
        {
            return Try(() => AddCore(canvas, tool, points, style, token, replaceIndex, text, number), token);
        }

        private static Task<object?> AddCore(AnnotationScriptCanvas canvas, ScreenshotTool tool,
            AnnotationDocumentPoint[] points, object? options, CancellationToken token, int? replaceIndex = null,
            string? text = null, int? number = null, AnnotationPixelPath? pixelPath = null)
        {
            var fields = Fields(options);
            var record = Record(tool, points, fields, text, number, pixelPath);
            return canvas.AddAsync(record, fields.GetValueOrDefault("image") ??
                                           (tool == ScreenshotTool.Watermark
                                               ? record.Properties.WatermarkImagePath
                                               : null), token, replaceIndex);
        }

        private static ScreenshotAnnotationRecord Record(ScreenshotTool tool, AnnotationDocumentPoint[] points,
            Dictionary<string, object?> fields, string? text = null, int? number = null,
            AnnotationPixelPath? pixelPath = null)
        {
            var defaults = ScreenshotToolCatalog.Get(tool).DefaultStyle;
            var properties = ReadProperties(fields.GetValueOrDefault("properties"), defaults.Properties);
            if (pixelPath.HasValue) properties.PixelPath = pixelPath.Value;
            if (number.HasValue) properties.Number = number.Value;
            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (tool)
            {
                case ScreenshotTool.Ellipse:
                    properties.GeometryShape = AnnotationGeometryShape.Ellipse;
                    break;
                case ScreenshotTool.Eraser when
                    !Fields(fields.GetValueOrDefault("properties")).ContainsKey("pixelPath"):
                    properties.PixelPath = AnnotationPixelPath.Brush;
                    break;
            }

            if (properties != properties.Normalize())
                throw new ArgumentException("Annotation properties are outside their supported ranges.");
            var offset = fields.GetValueOrDefault("magnifierOffset") is Dictionary<string, object?> point
                ? new AnnotationDocumentPoint(Number(point["x"]), Number(point["y"]))
                : default;
            var record = new ScreenshotAnnotationRecord
            {
                Tool = tool, Points = points,
                Color = Color.Parse(fields.GetValueOrDefault("color") as string ?? defaults.Color),
                Thickness = fields.GetValueOrDefault("thickness") is { } thickness
                    ? Number(thickness)
                    : defaults.Thickness,
                FontSize = fields.GetValueOrDefault("fontSize") is { } size ? Number(size) : defaults.FontSize,
                MosaicSize = fields.GetValueOrDefault("mosaicSize") is { } block ? Integer(block) : defaults.MosaicSize,
                Text = text ?? fields.GetValueOrDefault("text") as string ??
                    (tool == ScreenshotTool.Watermark ? properties.WatermarkText : ""),
                Properties = properties,
                MagnifierOffset = offset,
            };
            if (record.Thickness is < 1 or > 1024 || record.FontSize is < 1 or > 1024 ||
                record.MosaicSize is < 2 or > 28)
                throw new ArgumentOutOfRangeException(nameof(fields));
            var singlePoint = tool is ScreenshotTool.Text or ScreenshotTool.Number or ScreenshotTool.Watermark
                                  or ScreenshotTool.Brush ||
                              (tool is ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.SmartErase
                                   or ScreenshotTool.Eraser &&
                               properties.PixelPath == AnnotationPixelPath.Brush);
            var minimum = singlePoint ? 1 : 2;
            if (points.Length < minimum) throw new ArgumentException("Insufficient points for the annotation tool.");
            return record;
        }

        private static Dictionary<string, object?> WithImage(object? style, object? image)
        {
            var fields = new Dictionary<string, object?>(Fields(style), StringComparer.Ordinal) { ["image"] = image };
            return fields;
        }

        private static PrivacyCandidate[] ReadCandidates(object? value)
        {
            // ReSharper disable once UseCollectionExpression
            return ((List<object?>)value!).Select(item =>
            {
                var fields = (Dictionary<string, object?>)item!;
                var bounds = (Dictionary<string, object?>)fields["bounds"]!;
                return new PrivacyCandidate((string)fields["provider"]!, (PrivacyCandidateKind)fields["kind"]!,
                    new(Integer(bounds["x"]), Integer(bounds["y"]), Integer(bounds["width"]),
                        Integer(bounds["height"])),
                    (string)fields["text"]!, fields.GetValueOrDefault("boundsApproximate") is true);
            }).ToArray();
        }

        private static StaticImageSaveOptions ExportOptions(object? options)
        {
            var fields = Fields(options);
            return new()
            {
                Format = fields.GetValueOrDefault("format") as StaticImageFormat?,
                Quality = fields.GetValueOrDefault("quality") is { } value ? Integer(value) : 90,
                Background = fields.GetValueOrDefault("background") as string ?? "#ffffff",
                Overwrite = fields.GetValueOrDefault("overwrite") is true,
            };
        }

        private static AnnotationDocumentPoint[] Points(object? points)
        {
            // ReSharper disable once UseCollectionExpression
            return ((List<object?>)points!).Select(value =>
            {
                var fields = (Dictionary<string, object?>)value!;
                return new AnnotationDocumentPoint(Number(fields["x"]), Number(fields["y"]));
            }).ToArray();
        }

        private static Dictionary<string, object?> Fields(object? value)
        {
            return value as Dictionary<string, object?> ?? [];
        }

        private static double Number(object? value)
        {
            if (value is not double number || !double.IsFinite(number))
                throw new ArgumentException("A finite number is required.");
            return number;
        }

        private static int Integer(object? value)
        {
            var number = Number(value);
            if (!double.IsInteger(number) || number is < int.MinValue or > int.MaxValue)
                throw new ArgumentException("An integer is required.");
            return (int)number;
        }

        private static async ValueTask<object?> Try(Func<Task<object?>> operation, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                return new ScriptResult(true, await operation());
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                token.ThrowIfCancellationRequested();
                return new ScriptResult(false, ScriptServiceErrors.FromException(error));
            }
        }
    }
}

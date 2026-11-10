using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal static class ScreenshotToolCatalog
    {
        private static readonly AnnotationPropertyDefinition Width = new("AnnotationThickness", v => v.WidthInput());
        private static readonly AnnotationPropertyDefinition Color = new("AnnotationColor", v => v.Palette());

        private static readonly AnnotationPropertyDefinition
            Stroke = new("AnnotationStrokeStyle", v => v.StrokeStyle());

        private static readonly AnnotationPropertyDefinition Round = new("AnnotationCornerRadius",
            v => v.CornerRadius());

        private static readonly AnnotationPropertyDefinition Font = new("AnimationFontFamily", v => v.Font());
        private static readonly AnnotationPropertyDefinition Size = new("AnimationFontSize", v => v.FontSizeInput());

        public static IReadOnlyList<ScreenshotToolDefinition> All { get; } =
        [
            new(ScreenshotTool.Select, "ScreenshotSelectAnnotation", "ScreenshotSelectAnnotation",
                [], "Select", new()),
            new(ScreenshotTool.Rectangle, "ScreenshotAnnotationGeometry", "AnimationRectangle",
            [
                new("AnimationEllipse", v => v.GeometryShape()), new("AnnotationFilled", v => v.Filled()), Stroke,
                Width, Round, Color,
            ], "Geometry", new() { Thickness = 12, Color = "#ffff0f00" }),
            new(ScreenshotTool.Ellipse, "AnimationEllipse", "AnimationEllipse",
            [
                new("AnimationEllipse", v => v.GeometryShape()), new("AnnotationFilled", v => v.Filled()), Stroke,
                Width, Round, Color,
            ], "Geometry", new()
            {
                Thickness = 12, Color = "#ffff0f00",
                Properties = new() { GeometryShape = AnnotationGeometryShape.Ellipse },
            }),
            new(ScreenshotTool.Brush, "ScreenshotAnnotationPencil", "AnimationBrush", [Stroke, Width, Color], "Brush",
                new() { Thickness = 16, Color = "#ffff0f00" }),
            new(ScreenshotTool.Highlight, "ScreenshotAnnotationHighlighter", "ScreenshotHighlight",
                [new("ScreenshotHighlight", v => v.MarkerProperties())], "Marker",
                new() { Thickness = 12, Color = "#ffffff00" }) { SupportsRecording = false },
            new(ScreenshotTool.Arrow, "ScreenshotArrow", "ScreenshotArrow",
                [new("AnnotationArrowShape", v => v.ArrowShape()), Stroke, Width, Color], "Arrow",
                new() { Thickness = 12, Color = "#ffff0f00" }),
            new(ScreenshotTool.Line, "AnnotationToolLine", "AnnotationToolLine",
                [new("ScreenshotAnnotationBrokenLine", v => v.BrokenLineProperties())], "BrokenLine",
                new() { Thickness = 12, Color = "#ffff0f00" }),
            new(ScreenshotTool.Polyline, "ScreenshotAnnotationBrokenLine", "AnnotationToolPolyline",
                [new("ScreenshotAnnotationBrokenLine", v => v.BrokenLineProperties())], "BrokenLine",
                new() { Thickness = 12, Color = "#ffff0f00" }),
            new(ScreenshotTool.Text, "ScreenshotAnnotationText", "AnimationText", [
                new("AnimationBold", v => v.TextStyles()), new("AnnotationTextOutline", v => v.TextOutline()),
                new("AnnotationTextBackground", v => v.TextBackground()), Font, Size,
                Color,
            ], "Text", new()
            {
                FontSize = 30, Color = "#ffff0f00",
                Properties = new()
                {
                    Bold = true, TextOutlineEnabled = true, TextOutline = Colors.White,
                    TextOutlineWidth = 10, TextBackground = Colors.White, TextPadding = 10,
                },
            }),
            new(ScreenshotTool.Number, "ScreenshotAnnotationSerial", "AnnotationToolNumber",
                [new("AnnotationNextNumber", v => v.NumberProperties())], "Number",
                new()
                {
                    Thickness = 16, Color = "#ffff0f00",
                    Properties = new() { EndEndpoint = AnnotationEndpointStyle.NotchedArrow },
                }),
            new(ScreenshotTool.Mosaic, "AnimationMosaic", "AnimationMosaic",
                    [new("AnnotationMosaicSize", v => v.PixelToolProperties())], "Mosaic",
                    new() { Thickness = 12, Properties = new() { PixelPath = AnnotationPixelPath.Brush } })
                { SupportsRecording = false },
            new(ScreenshotTool.Blur, "AnimationBlur", "AnimationBlur",
                [new("AnnotationBlurRadius", v => v.PixelToolProperties())], "Mosaic", new()),
            new(ScreenshotTool.SmartErase, "AnnotationSmartErase", "AnnotationSmartErase",
                [new("AnnotationInpaintingRadius", v => v.PixelToolProperties())], "Mosaic", new()),
            new(ScreenshotTool.AutoMosaic, "AnnotationAutoMosaic", "AnnotationAutoMosaic",
                    [new("AnnotationAutoMosaic", v => v.AutoMosaicProperties())], "AutoMosaic", new())
                { SupportsRecording = false },
            new(ScreenshotTool.Eraser, "AnnotationToolEraser", "AnnotationToolEraser",
                [new("AnnotationToolEraser", v => v.PixelToolProperties())], "Eraser",
                new() { Thickness = 12, Properties = new() { PixelPath = AnnotationPixelPath.Brush } }),
            new(ScreenshotTool.StrokeEraser, "AnnotationToolStrokeEraser", "AnnotationToolStrokeEraser",
                [], "StrokeEraser", new()),
            new(ScreenshotTool.Spotlight, "AnnotationToolSpotlight", "AnnotationToolSpotlight",
                [new("AnnotationToolSpotlight", v => v.SpotlightProperties())], "Spotlight",
                new() { Thickness = 6, Properties = new() { Opacity = .7 } }),
            new(ScreenshotTool.Magnifier, "AnnotationToolMagnifier", "AnnotationToolMagnifier",
                    [new("AnnotationMagnification", v => v.MagnifierProperties())], "Magnifier",
                    new() { Thickness = 2 })
                { SupportsRecording = false },
            new(ScreenshotTool.Watermark, "AnnotationToolWatermark", "AnnotationToolWatermark",
            [
                new("AnnotationWatermarkInput", v => v.WatermarkProperties()),
            ], "Watermark", new()
            {
                Color = "#ffff0f00", FontSize = 18,
                Properties = new() { Tiled = true, Opacity = .3, TextPadding = 0 },
            }),
        ];

        public static IReadOnlyList<IReadOnlyList<ScreenshotTool>> DefaultGroups { get; } =
        [
            [ScreenshotTool.Rectangle, ScreenshotTool.Spotlight],
            [ScreenshotTool.Brush, ScreenshotTool.Highlight],
            [ScreenshotTool.Arrow, ScreenshotTool.Polyline, ScreenshotTool.Magnifier],
            [ScreenshotTool.Text, ScreenshotTool.Watermark],
            [ScreenshotTool.Number],
            [ScreenshotTool.Mosaic, ScreenshotTool.AutoMosaic],
            [ScreenshotTool.Eraser],
        ];

        public static IReadOnlyList<ScreenshotToolDefinition> Registered { get; } =
            [.. DefaultGroups.SelectMany(group => group).Select(Get)];

        public static bool IsRegistered(ScreenshotTool tool)
        {
            return tool is ScreenshotTool.Rectangle or ScreenshotTool.Spotlight or ScreenshotTool.Brush
                or ScreenshotTool.Highlight or ScreenshotTool.Arrow or ScreenshotTool.Polyline
                or ScreenshotTool.Magnifier or ScreenshotTool.Text or ScreenshotTool.Watermark
                or ScreenshotTool.Number or ScreenshotTool.Mosaic or ScreenshotTool.AutoMosaic or ScreenshotTool.Eraser;
        }

        public static ScreenshotTool CanonicalTool(ScreenshotTool tool)
        {
            return tool switch
            {
                ScreenshotTool.Ellipse => ScreenshotTool.Rectangle,
                ScreenshotTool.Line => ScreenshotTool.Polyline,
                ScreenshotTool.Blur or ScreenshotTool.SmartErase => ScreenshotTool.Mosaic,
                ScreenshotTool.StrokeEraser => ScreenshotTool.Eraser,
                _ => tool,
            };
        }

        public static bool IsStyleKey(string key)
        {
            return All.Any(definition => definition.Tool != ScreenshotTool.Select && definition.StyleKey == key);
        }

        public static ScreenshotToolDefinition Get(ScreenshotTool tool)
        {
            return All.First(definition => definition.Tool == tool);
        }
    }
}

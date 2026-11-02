namespace RitsukageGif.Desktop
{
    internal static class ScreenshotToolCatalog
    {
        private static readonly AnnotationPropertyDefinition Width = new("AnnotationThickness", v => v.WidthInput());
        private static readonly AnnotationPropertyDefinition Color = new("AnnotationColor", v => v.Palette());

        private static readonly AnnotationPropertyDefinition
            Stroke = new("AnnotationStrokeStyle", v => v.StrokeStyle());

        private static readonly AnnotationPropertyDefinition Curved = new("AnnotationCurved", v => v.Curved());

        private static readonly AnnotationPropertyDefinition StartEndpoint = new("AnnotationStartEndpoint",
            v => v.Endpoint(true));

        private static readonly AnnotationPropertyDefinition EndEndpoint = new("AnnotationEndEndpoint",
            v => v.Endpoint(false));

        private static readonly AnnotationPropertyDefinition Round = new("AnnotationCornerRadius",
            v => v.CornerRadius());

        private static readonly AnnotationPropertyDefinition Font = new("AnimationFontFamily", v => v.Font());
        private static readonly AnnotationPropertyDefinition Size = new("AnimationFontSize", v => v.FontSizeInput());
        private static readonly AnnotationPropertyDefinition Opacity = new("AnnotationOpacity", v => v.OpacityInput());

        public static IReadOnlyList<ScreenshotToolDefinition> All { get; } =
        [
            new(ScreenshotTool.Select, "ScreenshotSelectAnnotation", "ScreenshotSelectAnnotation",
                [], "Select", new()),
            new(ScreenshotTool.Rectangle, "AnimationRectangle", "AnimationRectangle",
            [
                new("AnimationEllipse", v => v.GeometryShape()), new("AnnotationFilled", v => v.Filled()), Stroke,
                Width, Round, Color,
            ], "Geometry", new()),
            new(ScreenshotTool.Ellipse, "AnimationEllipse", "AnimationEllipse",
            [
                new("AnimationEllipse", v => v.GeometryShape()), new("AnnotationFilled", v => v.Filled()), Stroke,
                Width, Round, Color,
            ], "Geometry", new() { Properties = new() { GeometryShape = AnnotationGeometryShape.Ellipse } }),
            new(ScreenshotTool.Brush, "AnimationBrush", "AnimationBrush", [Stroke, Width, Color], "Brush", new()),
            new(ScreenshotTool.Highlight, "ScreenshotHighlight", "ScreenshotHighlight",
                [new("ScreenshotHighlight", v => v.MarkerProperties())], "Marker",
                new() { Thickness = 12, Color = "#ffffff00" }),
            new(ScreenshotTool.Arrow, "ScreenshotArrow", "ScreenshotArrow",
                [new("AnnotationArrowShape", v => v.ArrowShape()), Curved, Stroke, Width, Color], "Arrow", new()),
            new(ScreenshotTool.Line, "AnnotationToolLine", "AnnotationToolLine",
                [Curved, StartEndpoint, EndEndpoint, Stroke, Width, Color], "BrokenLine", new()),
            new(ScreenshotTool.Polyline, "AnnotationToolPolyline", "AnnotationToolPolyline",
                [Curved, StartEndpoint, EndEndpoint, Stroke, Width, Color], "BrokenLine", new()),
            new(ScreenshotTool.Text, "AnimationText", "AnimationText", [
                new("AnimationBold", v => v.TextStyles()), new("AnnotationTextOutline", v => v.TextOutline()),
                new("AnnotationTextBackground", v => v.TextBackground()), Font, Size,
                new("AnnotationTextPadding", v => v.TextLayout()), Color,
            ], "Text", new()),
            new(ScreenshotTool.Number, "AnnotationToolNumber", "AnnotationToolNumber",
                [new("AnnotationNextNumber", v => v.NumberProperties())], "Number",
                new() { Properties = new() { EndEndpoint = AnnotationEndpointStyle.NotchedArrow } }),
            new(ScreenshotTool.Mosaic, "AnimationMosaic", "AnimationMosaic",
                [new("AnnotationMosaicSize", v => v.PixelToolProperties())], "Mosaic", new()),
            new(ScreenshotTool.Blur, "AnimationBlur", "AnimationBlur",
                [new("AnnotationBlurRadius", v => v.PixelToolProperties())], "Mosaic", new()),
            new(ScreenshotTool.SmartErase, "AnnotationSmartErase", "AnnotationSmartErase",
                [new("AnnotationInpaintingRadius", v => v.PixelToolProperties())], "Mosaic", new()),
            new(ScreenshotTool.Eraser, "AnnotationToolEraser", "AnnotationToolEraser",
                [new("AnnotationToolEraser", v => v.PixelToolProperties())], "Eraser",
                new() { Properties = new() { PixelPath = AnnotationPixelPath.Brush } }),
            new(ScreenshotTool.StrokeEraser, "AnnotationToolStrokeEraser", "AnnotationToolStrokeEraser",
                [], "StrokeEraser", new()),
            new(ScreenshotTool.Spotlight, "AnnotationToolSpotlight", "AnnotationToolSpotlight",
            [
                new("AnnotationSpotlightEllipse", v => v.SpotlightShape()), Round, Opacity,
                new("AnnotationSpotlightStroke", v => v.SpotlightStroke()), Width, Color,
            ], "Spotlight", new()),
            new(ScreenshotTool.Magnifier, "AnnotationToolMagnifier", "AnnotationToolMagnifier",
                [new("AnnotationMagnification", v => v.MagnifierProperties())], "Magnifier", new() { Thickness = 2 }),
            new(ScreenshotTool.Watermark, "AnnotationToolWatermark", "AnnotationToolWatermark",
            [
                new("AnnotationWatermarkImage", v => v.WatermarkSource()), Font, Size, Opacity,
                new("AnnotationNineGrid", v => v.WatermarkLayout()), Color,
            ], "Watermark", new()),
        ];

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

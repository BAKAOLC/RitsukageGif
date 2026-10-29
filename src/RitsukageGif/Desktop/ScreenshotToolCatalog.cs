namespace RitsukageGif.Desktop
{
    internal static class ScreenshotToolCatalog
    {
        private static readonly AnnotationPropertyDefinition Width = new("AnnotationThickness", v => v.WidthInput());
        private static readonly AnnotationPropertyDefinition Color = new("AnnotationColor", v => v.Palette());
        private static readonly AnnotationPropertyDefinition Dashed = new("AnnotationDashed", v => v.Dashed());
        private static readonly AnnotationPropertyDefinition Font = new("AnimationFontFamily", v => v.Font());
        private static readonly AnnotationPropertyDefinition Size = new("AnimationFontSize", v => v.FontSizeInput());
        private static readonly AnnotationPropertyDefinition Opacity = new("AnnotationOpacity", v => v.OpacityInput());

        public static IReadOnlyList<ScreenshotToolDefinition> All { get; } =
        [
            new(ScreenshotTool.Select, "ScreenshotSelectAnnotation", "ScreenshotSelectAnnotation",
                []),
            new(ScreenshotTool.Rectangle, "AnimationRectangle", "AnimationRectangle",
                [new("AnnotationFilled", v => v.Filled()), Dashed, Width, Color]),
            new(ScreenshotTool.Ellipse, "AnimationEllipse", "AnimationEllipse",
                [new("AnnotationFilled", v => v.Filled()), Dashed, Width, Color]),
            new(ScreenshotTool.Brush, "AnimationBrush", "AnimationBrush", [Width, Color]),
            new(ScreenshotTool.Highlight, "ScreenshotHighlight", "ScreenshotHighlight", [Width, Opacity, Color]),
            new(ScreenshotTool.Arrow, "ScreenshotArrow", "ScreenshotArrow", [Dashed, Width, Color]),
            new(ScreenshotTool.Line, "AnnotationToolLine", "AnnotationToolLine", [Dashed, Width, Color]),
            new(ScreenshotTool.Polyline, "AnnotationToolPolyline", "AnnotationToolPolyline",
                [Dashed, Width, new("AnnotationFinishPolyline", v => v.FinishPolyline()), Color]),
            new(ScreenshotTool.Text, "AnimationText", "AnimationText", [
                new("AnimationBold", v => v.TextStyles()), new("AnnotationTextOutline", v => v.TextOutline()),
                new("AnnotationTextBackground", v => v.TextBackground()), Font, Size,
                new("AnnotationTextPadding", v => v.TextLayout()), Color,
            ]),
            new(ScreenshotTool.Number, "AnnotationToolNumber", "AnnotationToolNumber",
                [new("AnnotationNextNumber", v => v.NextNumber()), Size, Color]),
            new(ScreenshotTool.Mosaic, "AnimationMosaic", "AnimationMosaic",
                [new("AnnotationMosaicSize", v => v.Mosaic())]),
            new(ScreenshotTool.Blur, "AnimationBlur", "AnimationBlur", [new("AnnotationBlurRadius", v => v.Blur())]),
            new(ScreenshotTool.Eraser, "AnnotationToolEraser", "AnnotationToolEraser", [Width]),
            new(ScreenshotTool.StrokeEraser, "AnnotationToolStrokeEraser", "AnnotationToolStrokeEraser",
                []),
            new(ScreenshotTool.Spotlight, "AnnotationToolSpotlight", "AnnotationToolSpotlight",
                [Opacity, Color]),
            new(ScreenshotTool.Magnifier, "AnnotationToolMagnifier", "AnnotationToolMagnifier",
                [new("AnnotationMagnification", v => v.Magnification()), Width]),
            new(ScreenshotTool.Watermark, "AnnotationToolWatermark", "AnnotationToolWatermark",
            [
                new("AnnotationWatermarkImage", v => v.WatermarkSource()), Font, Size, Opacity,
                new("AnnotationNineGrid", v => v.WatermarkLayout()), Color,
            ]),
        ];

        public static ScreenshotToolDefinition Get(ScreenshotTool tool)
        {
            return All.First(definition => definition.Tool == tool);
        }
    }
}

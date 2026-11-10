namespace RitsukageGif.Desktop
{
    public sealed partial record ScreenshotAnnotationProperties
    {
        public AnnotationNumberStyle NumberStyle { get; set; }
        public AnnotationNumberFormat NumberFormat { get; set; }
        public bool NumberCallout { get; set; }
        public bool NumberCaptionGuide { get; set; }
        public double NumberCaptionScale { get; set; } = .6;
        public bool NumberCaptionOutline { get; set; }

        partial void NormalizeNumberProperties()
        {
            NumberStyle = Enum.IsDefined(NumberStyle) ? NumberStyle : AnnotationNumberStyle.FilledCircle;
            NumberFormat = Enum.IsDefined(NumberFormat) ? NumberFormat : AnnotationNumberFormat.Decimal;
            NumberCaptionScale = double.IsFinite(NumberCaptionScale) ? Math.Clamp(NumberCaptionScale, .2, 1) : .6;
        }
    }
}

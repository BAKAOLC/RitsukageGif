namespace RitsukageGif.Desktop
{
    public sealed partial record ScreenshotAnnotationProperties
    {
        public bool TextOutlineEnabled { get; set; }
        public bool TextBackgroundEnabled { get; set; }
        public double TextBackgroundOpacity { get; set; } = 1;
        public double TextBackgroundRadius { get; set; } = 5;

        partial void NormalizeTextProperties()
        {
            TextBackgroundOpacity =
                double.IsFinite(TextBackgroundOpacity) ? Math.Clamp(TextBackgroundOpacity, 0, 1) : 1;
            TextBackgroundRadius = double.IsFinite(TextBackgroundRadius) ? Math.Clamp(TextBackgroundRadius, 0, 30) : 5;
        }
    }
}

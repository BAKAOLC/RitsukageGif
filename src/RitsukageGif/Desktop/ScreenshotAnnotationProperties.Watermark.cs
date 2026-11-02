namespace RitsukageGif.Desktop
{
    public sealed partial record ScreenshotAnnotationProperties
    {
        public string WatermarkText { get; set; } = "";
        public string? WatermarkImagePath { get; set; }
    }
}

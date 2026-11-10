namespace RitsukageGif.Desktop
{
    public sealed partial record ScreenshotAnnotationProperties
    {
        public string WatermarkText { get; set; } = "";
        public string? WatermarkImagePath { get; set; }
        public double WatermarkSpacing { get; set; } = 20;
        public double WatermarkAngle { get; set; } = 315;
        public string WatermarkTimeFormat { get; set; } = "$yyyy-MM-dd HH:mm:ss$";

        partial void NormalizeWatermarkProperties()
        {
            WatermarkSpacing = double.IsFinite(WatermarkSpacing) ? Math.Clamp(WatermarkSpacing, 0, 100) : 20;
            WatermarkAngle = double.IsFinite(WatermarkAngle) ? WatermarkAngle % 360 : 315;
        }
    }
}

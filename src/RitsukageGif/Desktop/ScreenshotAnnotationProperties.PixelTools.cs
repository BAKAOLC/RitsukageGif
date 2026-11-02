namespace RitsukageGif.Desktop
{
    public sealed partial record ScreenshotAnnotationProperties
    {
        public AnnotationPixelPath PixelPath { get; set; }
        public int InpaintingRadius { get; set; } = 4;

        partial void NormalizePixelToolProperties()
        {
            if (!Enum.IsDefined(PixelPath)) PixelPath = AnnotationPixelPath.Rectangle;
            InpaintingRadius = Math.Clamp(InpaintingRadius, 1, 32);
        }
    }
}

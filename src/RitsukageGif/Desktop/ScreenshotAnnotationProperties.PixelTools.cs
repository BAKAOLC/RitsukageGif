namespace RitsukageGif.Desktop
{
    public sealed partial record ScreenshotAnnotationProperties
    {
        public AnnotationPixelPath PixelPath { get; set; }
        public AnnotationMosaicMode MosaicMode { get; set; }
        public bool AutoMosaicSynchronous { get; set; } = true;
        public int InpaintingRadius { get; set; } = 4;

        partial void NormalizePixelToolProperties()
        {
            if (!Enum.IsDefined(PixelPath)) PixelPath = AnnotationPixelPath.Rectangle;
            if (!Enum.IsDefined(MosaicMode)) MosaicMode = AnnotationMosaicMode.Mosaic;
            InpaintingRadius = Math.Clamp(InpaintingRadius, 1, 32);
        }
    }
}

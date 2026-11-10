namespace RitsukageGif.Desktop
{
    public sealed partial record ScreenshotAnnotationProperties
    {
        public double Magnification { get; set; } = 1.5;
        public bool MagnifierEllipse { get; set; } = true;
        public bool MagnifierAntialias { get; set; } = true;
        public bool MagnifierShadow { get; set; }
        public bool MagnifierShowAnnotations { get; set; }
        public AnnotationMagnifierLink MagnifierLink { get; set; }

        partial void NormalizeMagnifierProperties()
        {
            Magnification = double.IsFinite(Magnification) ? Math.Clamp(Magnification, 1, 8) : 1.5;
            MagnifierLink = Enum.IsDefined(MagnifierLink) ? MagnifierLink : AnnotationMagnifierLink.Line;
        }
    }
}

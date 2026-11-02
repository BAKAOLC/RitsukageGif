namespace RitsukageGif.Desktop
{
    public sealed partial record ScreenshotAnnotationProperties
    {
        public AnnotationMarkerPath MarkerPath { get; set; }
        public AnnotationMarkerBlend MarkerBlend { get; set; }

        partial void NormalizeMarkerProperties()
        {
            if (!Enum.IsDefined(MarkerPath)) MarkerPath = AnnotationMarkerPath.Line;
            if (!Enum.IsDefined(MarkerBlend)) MarkerBlend = AnnotationMarkerBlend.Multiply;
        }
    }
}

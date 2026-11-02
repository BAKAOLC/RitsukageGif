namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private void RestoreHdrSamples(byte[] output, CapturedFrame destination, PixelRect globalRegion,
            CapturedFrame samples, PixelRect sampleRegion, ScreenshotAnnotation annotation, CancellationToken token)
        {
            if (annotation.Tool == ScreenshotTool.Eraser)
            {
                EraseHdrSamples(output, destination, globalRegion, samples, sampleRegion, annotation, token);
                return;
            }

            MagnifyHdrLayer(output, destination, globalRegion, samples, sampleRegion, annotation, token);
        }
    }
}

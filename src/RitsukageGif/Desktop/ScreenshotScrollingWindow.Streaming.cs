namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class ScreenshotScrollingWindow
    {
        internal Task PrepareStreamingAnnotationsAsync(ScreenshotAnnotationSession annotations, OutputColorMode mode,
            CancellationToken token)
        {
            return _session.PrepareStreamingAnnotationsAsync(annotations, mode, token);
        }

        internal void ClearStreamingAnnotations()
        {
            _session.ClearStreamingAnnotations();
        }
    }
}

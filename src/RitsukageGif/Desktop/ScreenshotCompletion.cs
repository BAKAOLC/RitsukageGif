namespace RitsukageGif.Desktop
{
    internal sealed record ScreenshotCompletion(
        SelectionResult? RecordingSelection,
        ScreenshotResult? Translation,
        TranslationMode TranslationMode,
        NativeCaptureSessionReference? SourceReference = null) : IDisposable
    {
        public void Dispose()
        {
            SourceReference?.Dispose();
        }
    }
}

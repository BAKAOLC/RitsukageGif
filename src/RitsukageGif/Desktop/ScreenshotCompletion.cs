namespace RitsukageGif.Desktop
{
    internal sealed record ScreenshotCompletion(
        SelectionResult? RecordingSelection,
        ReadOnlyMemory<byte> TranslationPng,
        NativeCaptureSessionReference? SourceReference = null) : IDisposable
    {
        public void Dispose()
        {
            SourceReference?.Dispose();
        }
    }
}

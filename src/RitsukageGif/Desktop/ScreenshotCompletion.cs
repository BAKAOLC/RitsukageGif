namespace RitsukageGif.Desktop
{
    internal sealed record ScreenshotCompletion(
        SelectionResult? RecordingSelection,
        ReadOnlyMemory<byte> TranslationPng);
}

namespace RitsukageGif.Desktop
{
    internal interface IScreenshotPasteTarget : IDisposable
    {
        ValueTask MarkClipboardCopiedAsync(CancellationToken token);

        Task TryPasteAsync(CancellationToken token);
    }
}

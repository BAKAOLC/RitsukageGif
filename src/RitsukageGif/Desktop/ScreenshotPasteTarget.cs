namespace RitsukageGif.Desktop
{
    internal sealed record ScreenshotPasteTarget(
        nint Window,
        nint Focus,
        uint ProcessId,
        uint ThreadId,
        WindowsFocusedElementIdentity Identity) : IScreenshotPasteTarget
    {
        private uint _clipboardSequence;

        public ValueTask MarkClipboardCopiedAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _clipboardSequence = ScreenshotPasteGuard.ClipboardSequence;
            return ValueTask.CompletedTask;
        }

        public Task TryPasteAsync(CancellationToken token)
        {
            return ScreenshotPasteGuard.TryPasteAsync(this, _clipboardSequence, token);
        }

        public void Dispose()
        {
        }
    }
}

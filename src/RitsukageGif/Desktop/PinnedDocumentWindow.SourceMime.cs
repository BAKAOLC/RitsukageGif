namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        private async Task CopySourceFilesAsync(IReadOnlyList<string> paths)
        {
            var transfer = CreateFilesTransfer(paths);
            Presentation.SourceMime?.ApplyTo(transfer.Items[0]);
            _lifetime.Token.ThrowIfCancellationRequested();
            var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            await clipboard.SetDataAsync(transfer);
            await clipboard.FlushAsync();
        }
    }
}

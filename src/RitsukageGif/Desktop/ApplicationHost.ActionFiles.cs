using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private async Task PinActionFilesAsync(IReadOnlyList<string> paths, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (IsClosing || IsSelecting || IsConfiguring)
                throw new InvalidOperationException(UiText.Get("CommandNotReady"));
            var existing = paths.Where(path => File.Exists(path) || Directory.Exists(path))
                .Select(Path.GetFullPath).ToArray();
            if (existing.Length == 0) throw new ArgumentException(UiText.Get("GlobalActionFileRequired"));
            if (!await _pinnedImages.PinFilesAsync(existing, token))
                throw new NotSupportedException(UiText.Get("DesktopToolbarDropUnsupported"));
        }

        private async Task PinScriptFilesAsync(IReadOnlyList<string> paths, CancellationToken token)
        {
            await Dispatcher.UIThread.InvokeAsync(() => PinActionFilesAsync(paths, token));
        }
    }
}

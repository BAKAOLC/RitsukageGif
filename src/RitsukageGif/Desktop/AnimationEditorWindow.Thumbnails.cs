using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationEditorWindow
    {
        private readonly AnimationEditorThumbnailCache _thumbnailCache = new();
        private bool _thumbnailPrefetchPending;
        private bool _thumbnailPrefetchRunning;

        private void RequestThumbnailPrefetch()
        {
            if (_languageClosed || _document is null) return;
            _thumbnailPrefetchPending = true;
            if (_thumbnailPrefetchRunning) return;
            _thumbnailPrefetchRunning = true;
            _ = PrefetchThumbnailsAsync();
        }

        private async Task PrefetchThumbnailsAsync()
        {
            try
            {
                while (_thumbnailPrefetchPending && !_languageClosed)
                {
                    _thumbnailPrefetchPending = false;
                    if (_document is not { } document) return;
                    var first = Math.Max(0, CurrentFrame - 12);
                    var paths = document.Frames.Skip(first).Take(49).Select(frame => frame.Path).ToArray();
                    await _thumbnailCache.PrefetchAsync(paths);
                }
            }
            catch (OperationCanceledException)
            {
                // A document replacement can invalidate the previous warmup.
            }
            finally
            {
                _thumbnailPrefetchRunning = false;
                if (_thumbnailPrefetchPending && !_languageClosed)
                    Dispatcher.UIThread.Post(RequestThumbnailPrefetch);
            }
        }
    }
}

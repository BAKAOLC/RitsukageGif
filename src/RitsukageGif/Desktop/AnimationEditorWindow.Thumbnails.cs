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
                    if (_document is not { } document || document.Frames.Count == 0) return;
                    var total = document.Frames.Count;
                    var first = (Math.Clamp(CurrentFrame, 0, total - 1) - Math.Min(12, total - 1) + total) % total;
                    var paths = new string[Math.Min(49, total)];
                    for (var index = 0; index < paths.Length; index++)
                        paths[index] = document.Frames[(first + index) % total].Path;
                    await _thumbnailCache.PrefetchAsync(paths);
                }
            }
            catch (OperationCanceledException)
            {
                // A document replacement can invalidate the previous warmup.
            }
            catch (Exception error)
            {
                Logger.ZLogError(error, $"Animation editor thumbnail warmup failed.");
                throw;
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

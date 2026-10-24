using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class AnimationEditorThumbnail : Image
    {
        private readonly string _path;
        private readonly AnimationEditorThumbnailCache _cache;
        private AnimationEditorThumbnailLease? _lease;
        private CancellationTokenSource? _loading;
        private int _generation;

        public AnimationEditorThumbnail(string path, AnimationEditorThumbnailCache cache)
        {
            _path = path;
            _cache = cache;
            Width = 76;
            Height = 48;
            Stretch = Stretch.Uniform;
        }

        protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            var generation = ++_generation;
            if (_cache.TryAcquire(_path, out var cached))
            {
                _lease = cached;
                Source = cached!.Bitmap;
                return;
            }

            using var loading = new CancellationTokenSource();
            _loading = loading;
            AnimationEditorThumbnailLease? lease = null;
            try
            {
                lease = await _cache.AcquireAsync(_path, loading.Token);
                if (generation != _generation || loading.IsCancellationRequested) return;
                _lease = lease;
                Source = lease?.Bitmap;
                lease = null;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                lease?.Dispose();
                if (ReferenceEquals(_loading, loading)) _loading = null;
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _generation++;
            _loading?.Cancel();
            Source = null;
            _lease?.Dispose();
            _lease = null;
            base.OnDetachedFromVisualTree(e);
        }
    }
}

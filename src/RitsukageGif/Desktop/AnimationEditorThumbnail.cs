using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class AnimationEditorThumbnail : Control
    {
        private readonly AnimationEditorThumbnailCache _cache;
        private readonly string _path;
        private bool _failed;
        private int _generation;
        private AnimationEditorThumbnailLease? _lease;
        private CancellationTokenSource? _loading;

        public AnimationEditorThumbnail(string path, AnimationEditorThumbnailCache cache)
        {
            _path = path;
            _cache = cache;
            Width = 76;
            Height = 48;
        }

        private IImage? Source { get; set; }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _ = LoadThumbnailSafelyAsync(++_generation);
        }

        private async Task LoadThumbnailSafelyAsync(int generation)
        {
            try
            {
                await LoadThumbnailAsync(generation);
            }
            catch (Exception)
            {
                if (generation != _generation) return;
                Source = null;
                _failed = true;
                InvalidateVisual();
            }
        }

        private async Task LoadThumbnailAsync(int generation)
        {
            _failed = false;
            if (_cache.TryAcquire(_path, out var cached))
            {
                _lease = cached;
                Source = cached!.Bitmap;
                InvalidateVisual();
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
                _failed = lease is null;
                InvalidateVisual();
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

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
            if (Source is { } source)
            {
                var size = source.Size;
                if (size.Width <= 0 || size.Height <= 0) return;
                var scale = Math.Min(Bounds.Width / size.Width, Bounds.Height / size.Height);
                var width = size.Width * scale;
                var height = size.Height * scale;
                context.DrawImage(source, new(size),
                    new((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height));
                return;
            }

            var bounds = new Rect(Bounds.Size).Deflate(2);
            context.DrawRectangle(AppTheme.SurfaceTint, new Pen(AppTheme.Outline), bounds, 3, 3);
            var center = bounds.Center;
            var pen = new Pen(AppTheme.Muted, 1.5);
            if (_failed)
            {
                context.DrawLine(pen, center + new Vector(-5, -5), center + new Vector(5, 5));
                context.DrawLine(pen, center + new Vector(5, -5), center + new Vector(-5, 5));
            }
            else
            {
                for (var i = -1; i <= 1; i++)
                    context.DrawEllipse(AppTheme.Muted, null, center + new Vector(i * 6, 0), 1.5, 1.5);
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

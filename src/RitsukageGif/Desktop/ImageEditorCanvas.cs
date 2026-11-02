using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed class ImageEditorCanvas : Control, IDisposable
    {
        private readonly ScreenshotTiledImage _source;
        private bool _nativePreviewVisible;
        private ScreenshotTiledImage? _preview;
        private Rect _previewBounds;

        internal ImageEditorCanvas(int width, int height, byte[] pixels)
        {
            Width = width;
            Height = height;
            IsHitTestVisible = false;
            _source = new(width, height, pixels, AlphaFormat.Unpremul);
        }

        internal bool HasPreview => _preview is not null;

        internal bool NativePreviewVisible
        {
            get => _nativePreviewVisible;
            set
            {
                _nativePreviewVisible = value;
                InvalidateVisual();
            }
        }

        public void Dispose()
        {
            _preview?.Dispose();
            _source.Dispose();
        }

        internal void SetVisibleRegion(Rect region)
        {
            _source.VisibleRegion = region;
            InvalidateVisual();
        }

        internal void SetPreview(Rect bounds, PixelSize size, byte[] pixels)
        {
            var preview = new ScreenshotTiledImage(size.Width, size.Height, pixels, AlphaFormat.Unpremul)
            {
                VisibleRegion = new(0, 0, size.Width, size.Height),
            };
            _preview?.Dispose();
            _preview = preview;
            _previewBounds = bounds;
            InvalidateVisual();
        }

        internal void ClearPreview()
        {
            _preview?.Dispose();
            _preview = null;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (_nativePreviewVisible) return;
            context.DrawRectangle(ImageTransparencyBackground.Checker, null, new(Bounds.Size));
            if (_preview is { } preview)
                context.DrawImage(preview, new(preview.Size), _previewBounds);
            else
                context.DrawImage(_source, new(_source.Size), new(Bounds.Size));
        }
    }
}

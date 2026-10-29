using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed class ScreenshotCursorOverlay : Control, IDisposable
    {
        private readonly WriteableBitmap? _bitmap, _hdrBitmap;
        private readonly PixelRect _monitor, _cursorBounds;

        internal ScreenshotCursorOverlay(FrozenScreenshotCursor cursor, PixelRect monitor, byte[] source, double scale,
            PixelRect? sourceRegion = null)
        {
            IsHitTestVisible = false;
            _monitor = monitor;
            Scale = scale;
            var sourceBounds = sourceRegion ?? monitor;
            _cursorBounds = cursor.Bounds.Intersect(monitor).Intersect(sourceBounds);
            if (_cursorBounds.IsEmpty) return;
            var stride = checked(_cursorBounds.Width * 4);
            var pixels = new byte[checked(stride * _cursorBounds.Height)];
            for (var y = 0; y < _cursorBounds.Height; y++)
                source.AsSpan(
                        ((_cursorBounds.Y - sourceBounds.Y + y) * sourceBounds.Width + _cursorBounds.X -
                         sourceBounds.X) * 4, stride)
                    .CopyTo(pixels.AsSpan(y * stride, stride));
            cursor.Draw(pixels, _cursorBounds);
            _bitmap = new(new(_cursorBounds.Width, _cursorBounds.Height), new(96, 96), PixelFormat.Bgra8888,
                AlphaFormat.Opaque);
            using var buffer = _bitmap.Lock();
            for (var y = 0; y < _cursorBounds.Height; y++)
                Marshal.Copy(pixels, y * stride, buffer.Address + y * buffer.RowBytes, stride);
            if (cursor.ReadTransparentPixels() is { } transparent)
            {
                _hdrBitmap = new(new(_cursorBounds.Width, _cursorBounds.Height), new(96, 96), PixelFormat.Bgra8888,
                    AlphaFormat.Unpremul);
                using var hdrBuffer = _hdrBitmap.Lock();
                for (var y = 0; y < _cursorBounds.Height; y++)
                    Marshal.Copy(transparent, ((_cursorBounds.Y - cursor.Bounds.Y + y) * cursor.Bounds.Width +
                        _cursorBounds.X - cursor.Bounds.X) * 4, hdrBuffer.Address + y * hdrBuffer.RowBytes, stride);
            }
        }

        internal bool NativeHdrBackground { get; set; }
        internal bool CanRenderHdr => _hdrBitmap is not null || _cursorBounds.IsEmpty;
        internal double Scale { get; set; }
        internal PixelRect Selection { get; set; }

        public void Dispose()
        {
            _bitmap?.Dispose();
            _hdrBitmap?.Dispose();
        }

        public override void Render(DrawingContext context)
        {
            var bitmap = NativeHdrBackground ? _hdrBitmap : _bitmap;
            if (bitmap is null) return;
            var selected = Selection.Intersect(_cursorBounds);
            if (selected.IsEmpty) return;
            context.DrawImage(bitmap,
                new(selected.X - _cursorBounds.X, selected.Y - _cursorBounds.Y, selected.Width, selected.Height),
                new((selected.X - _monitor.X) / Scale, (selected.Y - _monitor.Y) / Scale, selected.Width / Scale,
                    selected.Height / Scale));
        }
    }
}

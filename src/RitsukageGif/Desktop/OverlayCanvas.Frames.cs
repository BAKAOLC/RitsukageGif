using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayCanvas
    {
        internal void ReplacePixels(byte[] pixels)
        {
            ArgumentNullException.ThrowIfNull(pixels);
            if (_bitmap is ScreenshotTiledImage) throw new InvalidOperationException();
            var edges = new SelectionEdgeDetector(_monitor.Width, _monitor.Height, pixels);
            var bitmap = new WriteableBitmap(new(_monitor.Width, _monitor.Height), new(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
            try
            {
                using var buffer = bitmap.Lock();
                for (var y = 0; y < _monitor.Height; y++)
                    Marshal.Copy(pixels, y * _monitor.Width * 4,
                        buffer.Address + y * buffer.RowBytes, _monitor.Width * 4);
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }

            var previous = _bitmap;
            _bitmap = bitmap;
            _pixels = pixels;
            _edges = edges;
            ((IDisposable)previous).Dispose();
            InvalidateVisual();
        }
    }
}

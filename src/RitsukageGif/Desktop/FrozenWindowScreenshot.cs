using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed class FrozenWindowScreenshot : IDisposable
    {
        internal FrozenWindowScreenshot(WindowTarget target, PixelRect region, byte[] pixels, CapturedFrame? hdr = null)
        {
            Target = target;
            Region = region;
            Pixels = pixels;
            Hdr = hdr;
            Bitmap = new(new(region.Width, region.Height), new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using var buffer = Bitmap.Lock();
            for (var y = 0; y < region.Height; y++)
                Marshal.Copy(pixels, y * region.Width * 4, buffer.Address + y * buffer.RowBytes, region.Width * 4);
        }

        internal WindowTarget Target { get; }
        internal PixelRect Region { get; }
        internal byte[] Pixels { get; }
        internal CapturedFrame? Hdr { get; }
        internal WriteableBitmap Bitmap { get; }

        public void Dispose()
        {
            Bitmap.Dispose();
        }
    }
}

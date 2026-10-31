using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed class AnimationPinSurface(int width, int height) : IImage, IDisposable
    {
        private readonly WriteableBitmap _bitmap = new(new(width, height), new(96, 96), PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);

        public PixelRect Region { get; set; } = new(0, 0, width, height);

        public void Dispose()
        {
            _bitmap.Dispose();
        }

        public Size Size => new(Region.Width, Region.Height);

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
            context.DrawImage(_bitmap,
                new(sourceRect.X + Region.X, sourceRect.Y + Region.Y, sourceRect.Width, sourceRect.Height), destRect);
        }

        public void Update(PreviewFrame frame)
        {
            if (frame.Width != _bitmap.PixelSize.Width || frame.Height != _bitmap.PixelSize.Height)
                throw new InvalidDataException();
            using var buffer = _bitmap.Lock();
            for (var y = 0; y < frame.Height; y++)
                Marshal.Copy(frame.Pixels, y * frame.Width * 4, buffer.Address + y * buffer.RowBytes, frame.Width * 4);
        }
    }
}

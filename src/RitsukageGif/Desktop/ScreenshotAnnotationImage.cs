using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed class ScreenshotAnnotationImage(Bitmap bitmap, byte[]? encoded = null) : IDisposable
    {
        private int _references = 1;
        public Bitmap Bitmap { get; } = bitmap;
        public byte[]? Encoded { get; } = encoded;

        public void Dispose()
        {
            if (--_references == 0) Bitmap.Dispose();
        }

        public ScreenshotAnnotationImage Retain()
        {
            _references++;
            return this;
        }
    }
}

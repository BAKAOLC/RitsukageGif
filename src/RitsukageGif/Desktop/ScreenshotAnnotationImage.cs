using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed class ScreenshotAnnotationImage(Bitmap bitmap, byte[]? encoded = null) : IDisposable
    {
        private int _references = 1;
        internal string Id { get; init; } = Guid.NewGuid().ToString("N");
        public Bitmap Bitmap { get; } = bitmap;
        public byte[]? Encoded { get; private set; } = encoded;

        public void Dispose()
        {
            if (--_references == 0) Bitmap.Dispose();
        }

        internal byte[] GetEncoded()
        {
            if (Encoded is { } data) return data;
            using var stream = new MemoryStream();
            Bitmap.Save(stream, PngBitmapEncoderOptions.Default);
            return Encoded = stream.ToArray();
        }

        public ScreenshotAnnotationImage Retain()
        {
            _references++;
            return this;
        }
    }
}

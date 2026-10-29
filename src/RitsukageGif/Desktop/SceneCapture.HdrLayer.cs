using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SceneCapture
    {
        private sealed class HdrLayer : IDisposable
        {
            public bool IsHdrSource;
            public bool Rendered;
            public SKBitmap? Source;

            public HdrLayer(int width, int height)
            {
                Bitmap = CreateHdrBitmap(width, height);
                Drawing = new(Bitmap);
            }

            public SKBitmap Bitmap { get; }
            public SKCanvas Drawing { get; }

            public void Dispose()
            {
                Source?.Dispose();
                Drawing.Dispose();
                Bitmap.Dispose();
            }
        }
    }
}

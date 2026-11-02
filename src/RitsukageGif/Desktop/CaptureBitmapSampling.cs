using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal static class CaptureBitmapSampling
    {
        private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

        extension(SKCanvas canvas)
        {
            internal void DrawSampledBitmap(SKBitmap bitmap, SKRect destination, SKPaint paint)
            {
                using var image = SKImage.FromBitmap(bitmap);
                canvas.DrawImage(image, destination, Sampling, paint);
            }

            internal void DrawSampledBitmap(SKBitmap bitmap, SKRect source, SKRect destination, SKPaint paint)
            {
                using var image = SKImage.FromBitmap(bitmap);
                canvas.DrawImage(image, source, destination, Sampling, paint);
            }
        }
    }
}

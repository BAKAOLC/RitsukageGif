using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal static class CaptureFrameDrawing
    {
        public static void Draw(SKCanvas drawing, SKPaint paint, SKBitmap bitmap, PixelRect bounds,
            PixelRect region, int width, int height)
        {
            var clipped = bounds.Intersect(region);
            if (clipped.IsEmpty || region.IsEmpty) return;
            var scaleX = (float)bitmap.Width / bounds.Width;
            var scaleY = (float)bitmap.Height / bounds.Height;
            var outputX = (float)width / region.Width;
            var outputY = (float)height / region.Height;
            drawing.DrawBitmap(bitmap,
                new((clipped.X - bounds.X) * scaleX, (clipped.Y - bounds.Y) * scaleY,
                    (clipped.Right - bounds.X) * scaleX, (clipped.Bottom - bounds.Y) * scaleY),
                new SKRect((clipped.X - region.X) * outputX, (clipped.Y - region.Y) * outputY,
                    (clipped.Right - region.X) * outputX, (clipped.Bottom - region.Y) * outputY), paint);
        }
    }
}

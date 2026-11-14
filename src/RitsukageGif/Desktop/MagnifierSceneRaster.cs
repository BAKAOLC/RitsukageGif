using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal static class MagnifierSceneRaster
    {
        internal static Rect Region(Point point, Size sourceSize)
        {
            var horizontal = (int)Math.Ceiling(sourceSize.Width / 2) + 2;
            var vertical = (int)Math.Ceiling(sourceSize.Height / 2) + 2;
            return new(point.X - horizontal, point.Y - vertical,
                horizontal * 2 + 1, vertical * 2 + 1);
        }

        internal static void Draw(DrawingContext context, Rect source, Rect destination, Rect region,
            Action<DrawingContext, Rect> paint)
        {
            using var raster = new RenderTargetBitmap(new((int)region.Width, (int)region.Height), new(96, 96));
            using (var drawing = raster.CreateDrawingContext()) paint(drawing, region);
            context.DrawImage(raster,
                new(source.X - region.X, source.Y - region.Y, source.Width, source.Height), destination);
        }
    }
}

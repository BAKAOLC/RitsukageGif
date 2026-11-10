using Avalonia;

namespace RitsukageGif.Desktop
{
    internal static class PinnedViewportGeometry
    {
        internal const int DefaultMaximumWindowSize = 12000;
        internal const int MinimumWindowSizeSetting = 100;
        internal const int MaximumWindowSizeSetting = 100000;
        internal const int DefaultThumbnailSourceSize = 160;

        internal static Size MaximumViewport(int configuredPhysicalPixels, double renderScaling, PixelSize workingArea)
        {
            var maximum = Math.Max(configuredPhysicalPixels, MinimumWindowSizeSetting);
            return new(Math.Min(maximum, workingArea.Width * 0.9) / renderScaling,
                Math.Min(maximum, workingArea.Height * 0.9) / renderScaling);
        }

        internal static Point SourcePoint(PixelRect region, double zoom, double renderScaling, Vector scroll,
            Point cursor)
        {
            return new(region.X + (cursor.X + scroll.X) * renderScaling / zoom,
                region.Y + (cursor.Y + scroll.Y) * renderScaling / zoom);
        }

        internal static double MinimumThumbnailScale(PixelSize sourcePhysical, PixelSize standardDip,
            double renderScaling)
        {
            return Math.Min(standardDip.Width, standardDip.Height) * renderScaling /
                   Math.Min(sourcePhysical.Width, sourcePhysical.Height);
        }

        internal static PixelSize ThumbnailSourceSize(PixelSize standardDip, double renderScaling,
            double actualScale)
        {
            return new(
                (int)Math.Clamp(Math.Round(standardDip.Width * renderScaling / actualScale,
                    MidpointRounding.AwayFromZero), 1, int.MaxValue),
                (int)Math.Clamp(Math.Round(standardDip.Height * renderScaling / actualScale,
                    MidpointRounding.AwayFromZero), 1, int.MaxValue));
        }

        internal static PixelRect QuickThumbnail(PixelSize image, Point cursor, PixelSize size)
        {
            var width = Math.Min(size.Width, image.Width);
            var height = Math.Min(size.Height, image.Height);
            var x = (int)Math.Clamp(Math.Floor(cursor.X - width / 2d), 0, image.Width - width);
            var y = (int)Math.Clamp(Math.Floor(cursor.Y - height / 2d), 0, image.Height - height);
            return new(x, y, width, height);
        }
    }
}

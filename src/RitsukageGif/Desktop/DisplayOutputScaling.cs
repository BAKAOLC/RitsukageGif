using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal static class DisplayOutputScaling
    {
        internal static double ForRegion(Screens screens, PixelRect region)
        {
            if (DesktopCapture.UsesSourceSelection) return NativeCaptureSession.SharedOutputScaling;
            if (OperatingSystem.IsMacOS()) return NativeDisplayCatalog.Density(region);
            long largest = 0;
            double scale = 1;
            foreach (var screen in screens.All)
            {
                var bounds = screen.Bounds;
                var overlap = region.Intersect(new(bounds.X, bounds.Y, bounds.Width, bounds.Height));
                var area = (long)overlap.Width * overlap.Height;
                if (area <= largest) continue;
                largest = area;
                scale = screen.Scaling;
            }

            return scale is >= 1 and <= 16 ? scale : 1;
        }
    }
}

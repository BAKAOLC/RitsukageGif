namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class ScreenshotScrollingWindow
    {
        internal static List<PixelRect> CreateBorderRectangles(PixelRect region, IReadOnlyList<PixelRect> monitors,
            int thickness = 2)
        {
            var rectangles = new List<PixelRect>();
            foreach (var monitor in monitors)
            {
                if (region.Intersect(monitor).IsEmpty) continue;
                var left = Math.Max(region.X - thickness, monitor.X);
                var right = Math.Min(region.Right + thickness, monitor.Right);
                if (region.Y >= monitor.Y && region.Y < monitor.Bottom)
                    Add(new(left, region.Y - thickness >= monitor.Y ? region.Y - thickness : region.Y, right - left,
                        thickness));
                if (region.Bottom > monitor.Y && region.Bottom <= monitor.Bottom)
                    Add(new(left,
                        region.Bottom + thickness <= monitor.Bottom ? region.Bottom : region.Bottom - thickness,
                        right - left, thickness));
                var top = Math.Max(region.Y, monitor.Y);
                var bottom = Math.Min(region.Bottom, monitor.Bottom);
                if (region.X >= monitor.X && region.X < monitor.Right)
                    Add(new(region.X - thickness >= monitor.X ? region.X - thickness : region.X, top, thickness,
                        bottom - top));
                if (region.Right > monitor.X && region.Right <= monitor.Right)
                    Add(new(region.Right + thickness <= monitor.Right ? region.Right : region.Right - thickness, top,
                        thickness,
                        bottom - top));
            }

            return rectangles;

            void Add(PixelRect rectangle)
            {
                if (!rectangle.IsEmpty && !rectangles.Contains(rectangle)) rectangles.Add(rectangle);
            }
        }
    }
}

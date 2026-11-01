namespace RitsukageGif.Desktop
{
    internal static class SelectionMeasurements
    {
        internal static (PixelRect Bounds, string Kind, bool Right, bool Bottom) Reference(PixelRect region,
            PixelRect screen, PixelRect window, PixelRect control, PixelPoint pointer)
        {
            var reference = !control.IsEmpty && control != region && control.Contains(pointer)
                ? (control, "MeasurementControl")
                : !window.IsEmpty && window != region && window.Contains(pointer)
                    ? (window, "MeasurementWindow")
                    : (screen, "MeasurementScreen");
            return (reference.Item1, reference.Item2,
                pointer.X >= region.X + region.Width / 2, pointer.Y >= region.Y + region.Height / 2);
        }

        internal static IReadOnlyList<SelectionMeasurement> Create(PixelRect region, PixelRect reference,
            string kind, bool right, bool bottom)
        {
            if (region.IsEmpty) return [];
            var lines = new List<SelectionMeasurement>
            {
                new(new(region.X, region.Y), new(region.Right, region.Y), $"{region.Width} px"),
                new(new(region.X, region.Y), new(region.X, region.Bottom), $"{region.Height} px"),
            };
            if (reference.IsEmpty) return lines;
            var name = UiText.Get(kind);
            if (reference.Intersect(region) == region)
            {
                var x = region.X + region.Width / 2;
                var y = region.Y + region.Height / 2;
                Add(new(right ? region.Right : reference.X, y), new(right ? reference.Right : region.X, y),
                    right ? reference.Right - region.Right : region.X - reference.X);
                Add(new(x, bottom ? region.Bottom : reference.Y), new(x, bottom ? reference.Bottom : region.Y),
                    bottom ? reference.Bottom - region.Bottom : region.Y - reference.Y);
            }
            else
            {
                // The closest segment to the hovered target, including a diagonal when both axes are disjoint.
                var ax = Math.Clamp(reference.X, region.X, region.Right);
                var ay = Math.Clamp(reference.Y, region.Y, region.Bottom);
                var bx = Math.Clamp(ax, reference.X, reference.Right);
                var by = Math.Clamp(ay, reference.Y, reference.Bottom);
                var length = Math.Sqrt((double)(bx - ax) * (bx - ax) + (double)(by - ay) * (by - ay));
                if (length > 0) lines.Add(new(new(ax, ay), new(bx, by), $"{name} {length:0.#} px"));
            }

            return lines;

            void Add(PixelPoint start, PixelPoint end, int length)
            {
                if (length > 0) lines.Add(new(start, end, $"{name} {length} px"));
            }
        }
    }
}

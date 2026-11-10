namespace RitsukageGif.Desktop
{
    internal static partial class PinnedCompactLayout
    {
        internal static bool TryArrangeCompact(IReadOnlyList<PixelRect> packingBounds,
            PixelRect availableScreen, out PixelPoint[] positions)
        {
            positions = [];
            if (packingBounds.Count == 0 || availableScreen.IsEmpty ||
                packingBounds.Any(rectangle => rectangle.IsEmpty)) return false;
            var widest = packingBounds.Max(rectangle => rectangle.Width);
            if (widest > availableScreen.Width ||
                packingBounds.Max(rectangle => rectangle.Height) > availableScreen.Height)
                return false;
            var origin = Union(packingBounds);
            var normalized = packingBounds.Select(rectangle => rectangle with
            {
                X = rectangle.X - origin.X, Y = rectangle.Y - origin.Y,
            }).ToArray();
            var order = SpatialOrder(normalized);
            PixelRect[]? best = null;
            var bestArea = double.MaxValue;
            foreach (var width in TrialWidths(widest, availableScreen.Width))
            {
                var trial = SeedRows(normalized, order, width);
                Compact(trial);
                var extent = Union(trial);
                var area = (double)extent.Width * extent.Height;
                if (extent.Width > availableScreen.Width || extent.Height > availableScreen.Height || area >= bestArea)
                    continue;
                bestArea = area;
                best = trial;
            }

            if (best is null) return false;
            // The caller also compacts the selected winner before applying positions.
            Compact(best);
            var finalBounds = Union(best);
            var centerX = ((long)origin.X * 2 + origin.Width - 1) / 2;
            var centerY = ((long)origin.Y * 2 + origin.Height - 1) / 2;
            var x = Math.Clamp(centerX - finalBounds.Width * .5, availableScreen.X,
                (double)availableScreen.Right - finalBounds.Width);
            var y = Math.Clamp(centerY - finalBounds.Height * .5, availableScreen.Y,
                (double)availableScreen.Bottom - finalBounds.Height);
            positions = [.. best.Select(rectangle => new PixelPoint(Round(rectangle.X + x), Round(rectangle.Y + y)))];
            return true;
        }

        private static int Round(double value)
        {
            return (int)Math.Floor(value + .5);
        }

        private static int[] TrialWidths(int minimum, int maximum)
        {
            if (minimum <= 0 || maximum < minimum) return [];
            return
            [
                .. Enumerable.Range(0, 17).Select(step => Round(minimum + (maximum - minimum) * (step / 16d)))
                    .Distinct().Order(),
            ];
        }

        private static PixelRect[] SeedRows(PixelRect[] bounds, int[] order, int width)
        {
            var result = new PixelRect[bounds.Length];
            var x = 0;
            var y = 0;
            var height = 0;
            foreach (var index in order)
            {
                var rectangle = bounds[index];
                if (x > 0 && rectangle.Width > width - x)
                {
                    y = y + height + 1;
                    x = 0;
                    height = 0;
                }

                result[index] = rectangle with { X = x, Y = y };
                x += rectangle.Width;
                height = Math.Max(height, rectangle.Height);
            }

            return result;
        }

        private static PixelRect Union(IReadOnlyList<PixelRect> bounds)
        {
            var left = bounds.Min(rectangle => rectangle.X);
            var top = bounds.Min(rectangle => rectangle.Y);
            var right = bounds.Max(rectangle => rectangle.Right);
            var bottom = bounds.Max(rectangle => rectangle.Bottom);
            return new(left, top, right - left, bottom - top);
        }
    }
}

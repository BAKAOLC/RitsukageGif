namespace RitsukageGif.Desktop
{
    internal static partial class PinnedCompactLayout
    {
        private static int[] SpatialOrder(PixelRect[] bounds)
        {
            var initial = Enumerable.Range(0, bounds.Length).OrderBy(index => bounds[index].Y)
                .ThenBy(index => CenterY(bounds[index])).ThenBy(index => bounds[index].X)
                .ThenBy(index => CenterX(bounds[index])).ThenBy(index => index);
            var bands = new List<CompactSpatialBand>();
            foreach (var index in initial)
            {
                var rectangle = bounds[index];
                CompactSpatialBand? selected = null;
                var bestRatio = -1d;
                var bestDistance = long.MaxValue;
                foreach (var band in bands)
                {
                    var overlap = Math.Max(0,
                        Math.Min(band.Bounds.Bottom, rectangle.Bottom) - Math.Max(band.Bounds.Y, rectangle.Y));
                    var ratio = overlap / (double)Math.Max(1, Math.Min(band.Bounds.Height, rectangle.Height));
                    if (ratio < .4) continue;
                    var distance = Math.Abs(CenterY(rectangle) - CenterY(band.Bounds));
                    if (ratio > bestRatio || (FuzzyEqual(ratio, bestRatio) &&
                                              (distance < bestDistance || (distance == bestDistance &&
                                                                           selected is not null &&
                                                                           band.Bounds.Y < selected.Bounds.Y))))
                    {
                        selected = band;
                        bestRatio = ratio;
                        bestDistance = distance;
                    }
                }

                if (selected is null)
                {
                    selected = new(rectangle);
                    bands.Add(selected);
                }
                else
                {
                    selected.Bounds = Union([selected.Bounds, rectangle]);
                }

                selected.Indices.Add(index);
            }

            return
            [
                .. bands.OrderBy(band => band.Bounds.Y).ThenBy(band => band.Bounds.X)
                    .SelectMany(band => band.Indices.OrderBy(index => bounds[index].X)
                        .ThenBy(index => CenterX(bounds[index])).ThenBy(index => bounds[index].Y)
                        .ThenBy(index => index)),
            ];
        }

        private static bool FuzzyEqual(double first, double second)
        {
            return Math.Abs(first - second) * 1e12 <= Math.Min(Math.Abs(first), Math.Abs(second));
        }

        private static long CenterX(PixelRect bounds)
        {
            return ((long)bounds.X * 2 + bounds.Width - 1) / 2;
        }

        private static long CenterY(PixelRect bounds)
        {
            return ((long)bounds.Y * 2 + bounds.Height - 1) / 2;
        }
    }
}

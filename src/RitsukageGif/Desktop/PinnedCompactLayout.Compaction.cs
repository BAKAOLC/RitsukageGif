namespace RitsukageGif.Desktop
{
    internal static partial class PinnedCompactLayout
    {
        private static void Compact(PixelRect[] bounds)
        {
            for (var pass = 0; pass < Math.Max(2, bounds.Length * 2); pass++)
            {
                var changed = false;
                foreach (var index in Enumerable.Range(0, bounds.Length).OrderBy(index => bounds[index].X)
                             .ThenBy(index => bounds[index].Y).ToArray())
                {
                    var rectangle = bounds[index];
                    var left = 0;
                    for (var other = 0; other < bounds.Length; other++)
                    {
                        if (other == index) continue;
                        var obstacle = bounds[other];
                        if (obstacle.Y < rectangle.Bottom && rectangle.Y < obstacle.Bottom &&
                            obstacle.Right <= rectangle.X)
                            left = Math.Max(left, obstacle.Right);
                    }

                    if (left >= rectangle.X) continue;
                    bounds[index] = rectangle with { X = left };
                    changed = true;
                }

                foreach (var index in Enumerable.Range(0, bounds.Length).OrderBy(index => bounds[index].Y)
                             .ThenBy(index => bounds[index].X).ToArray())
                {
                    var rectangle = bounds[index];
                    var top = 0;
                    for (var other = 0; other < bounds.Length; other++)
                    {
                        if (other == index) continue;
                        var obstacle = bounds[other];
                        if (obstacle.X < rectangle.Right && rectangle.X < obstacle.Right &&
                            obstacle.Bottom <= rectangle.Y)
                            top = Math.Max(top, obstacle.Bottom);
                    }

                    if (top >= rectangle.Y) continue;
                    bounds[index] = rectangle with { Y = top };
                    changed = true;
                }

                if (!changed) break;
            }

            var extent = Union(bounds);
            if (extent is { X: 0, Y: 0 }) return;
            for (var index = 0; index < bounds.Length; index++)
                bounds[index] = bounds[index] with { X = bounds[index].X - extent.X, Y = bounds[index].Y - extent.Y };
        }
    }
}

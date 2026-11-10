namespace RitsukageGif.Desktop
{
    internal static class PinnedReferenceSelectionLayout
    {
        internal static PixelPoint[] Align(IReadOnlyList<PixelRect> bounds, int reference,
            PinnedImageArrangement arrangement)
        {
            var positions = Positions(bounds);
            var anchor = bounds[reference];
            for (var index = 0; index < bounds.Count; index++)
            {
                if (index == reference) continue;
                var rectangle = bounds[index];
                // ReSharper disable once SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault
                positions[index] = arrangement switch
                {
                    PinnedImageArrangement.AlignTop => new(rectangle.X, anchor.Y),
                    PinnedImageArrangement.AlignMiddle => new(rectangle.X,
                        Round(anchor.Y + anchor.Height * 0.5 - rectangle.Height * 0.5)),
                    PinnedImageArrangement.AlignBottom => new(rectangle.X,
                        anchor.Y + anchor.Height - rectangle.Height),
                    PinnedImageArrangement.AlignLeft => new(anchor.X, rectangle.Y),
                    PinnedImageArrangement.AlignRight => new(anchor.X + anchor.Width - rectangle.Width,
                        rectangle.Y),
                    _ => throw new ArgumentOutOfRangeException(nameof(arrangement)),
                };
            }

            return positions;
        }

        internal static double DistributionSpacing(IReadOnlyList<PixelRect> bounds, bool horizontal)
        {
            var ordered = Order(bounds, horizontal);
            var first = bounds[ordered[0]];
            var last = bounds[ordered[^1]];
            var extent = horizontal ? last.X + last.Width - first.X : last.Y + last.Height - first.Y;
            var occupied = bounds.Sum(rectangle => horizontal ? rectangle.Width : rectangle.Height);
            return (double)(extent - occupied) / (bounds.Count - 1);
        }

        internal static PixelPoint[] Distribute(IReadOnlyList<PixelRect> bounds, bool horizontal)
        {
            var positions = Positions(bounds);
            var ordered = Order(bounds, horizontal);
            var first = bounds[ordered[0]];
            var spacing = DistributionSpacing(bounds, horizontal);
            var cursor = horizontal ? first.X + first.Width : first.Y + first.Height;
            double next = cursor;
            for (var index = 1; index < ordered.Length - 1; index++)
            {
                var item = ordered[index];
                var rectangle = bounds[item];
                next += spacing;
                positions[item] = horizontal ? new(Round(next), rectangle.Y) : new(rectangle.X, Round(next));
                next += horizontal ? rectangle.Width : rectangle.Height;
            }

            return positions;
        }

        private static int[] Order(IReadOnlyList<PixelRect> bounds, bool horizontal)
        {
            return
            [
                .. Enumerable.Range(0, bounds.Count)
                    .OrderBy(index => horizontal ? bounds[index].X : bounds[index].Y)
                    .ThenBy(index => horizontal ? bounds[index].Y : bounds[index].X),
            ];
        }

        private static PixelPoint[] Positions(IEnumerable<PixelRect> bounds)
        {
            return [.. bounds.Select(rectangle => new PixelPoint(rectangle.X, rectangle.Y))];
        }

        private static int Round(double value)
        {
            return (int)Math.Floor(value + 0.5);
        }
    }
}

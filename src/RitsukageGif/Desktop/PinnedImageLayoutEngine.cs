namespace RitsukageGif.Desktop
{
    internal static class PinnedImageLayoutEngine
    {
        internal static PixelPoint[] Arrange(IReadOnlyList<PixelRect> bounds, PinnedImageArrangement arrangement,
            int maximumRowWidth)
        {
            ArgumentNullException.ThrowIfNull(bounds);
            if (!Enum.IsDefined(arrangement)) throw new ArgumentOutOfRangeException(nameof(arrangement));
            if (bounds.Count < 2 || bounds.Any(rectangle => rectangle.IsEmpty))
                throw new ArgumentException(null, nameof(bounds));
            var left = bounds.Min(rectangle => (long)rectangle.X);
            var top = bounds.Min(rectangle => (long)rectangle.Y);
            var right = bounds.Max(rectangle => (long)rectangle.X + rectangle.Width);
            var bottom = bounds.Max(rectangle => (long)rectangle.Y + rectangle.Height);
            var positions = bounds.Select(rectangle => new PixelPoint(rectangle.X, rectangle.Y)).ToArray();
            switch (arrangement)
            {
                case PinnedImageArrangement.AlignLeft:
                case PinnedImageArrangement.AlignCenter:
                case PinnedImageArrangement.AlignRight:
                case PinnedImageArrangement.AlignTop:
                case PinnedImageArrangement.AlignMiddle:
                case PinnedImageArrangement.AlignBottom:
                    for (var index = 0; index < bounds.Count; index++)
                    {
                        var rectangle = bounds[index];
                        // ReSharper disable once SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault
                        positions[index] = arrangement switch
                        {
                            PinnedImageArrangement.AlignLeft => Point(left, rectangle.Y),
                            PinnedImageArrangement.AlignCenter => Point(left + (right - left - rectangle.Width) / 2,
                                rectangle.Y),
                            PinnedImageArrangement.AlignRight => Point(right - rectangle.Width, rectangle.Y),
                            PinnedImageArrangement.AlignTop => Point(rectangle.X, top),
                            PinnedImageArrangement.AlignMiddle => Point(rectangle.X,
                                top + (bottom - top - rectangle.Height) / 2),
                            PinnedImageArrangement.AlignBottom => Point(rectangle.X, bottom - rectangle.Height),
                            _ => throw new ArgumentOutOfRangeException(nameof(arrangement)),
                        };
                    }

                    break;
                case PinnedImageArrangement.DistributeHorizontal:
                case PinnedImageArrangement.DistributeVertical:
                    if (bounds.Count < 3) return positions;
                    var horizontal = arrangement == PinnedImageArrangement.DistributeHorizontal;
                    var ordered = Enumerable.Range(0, bounds.Count)
                        .OrderBy(index => horizontal ? bounds[index].X : bounds[index].Y).ToArray();
                    var used = bounds.Sum(rectangle => horizontal ? (long)rectangle.Width : rectangle.Height);
                    var available = (horizontal ? right - left : bottom - top) - used;
                    long prefix = 0;
                    for (var index = 0; index < ordered.Length; index++)
                    {
                        var item = ordered[index];
                        var offset = (long)Math.Round((double)available * index / (ordered.Length - 1));
                        positions[item] = horizontal
                            ? Point(left + prefix + offset, bounds[item].Y)
                            : Point(bounds[item].X, top + prefix + offset);
                        prefix += horizontal ? bounds[item].Width : bounds[item].Height;
                    }

                    break;
                case PinnedImageArrangement.CompactHorizontal:
                case PinnedImageArrangement.CompactVertical:
                    long cursor = 0;
                    for (var index = 0; index < bounds.Count; index++)
                    {
                        var item = bounds[index];
                        positions[index] = arrangement == PinnedImageArrangement.CompactHorizontal
                            ? Point(left + cursor, top)
                            : Point(left, top + cursor);
                        cursor += arrangement == PinnedImageArrangement.CompactHorizontal ? item.Width : item.Height;
                    }

                    break;
                case PinnedImageArrangement.Compact:
                    var widest = bounds.Max(rectangle => rectangle.Width);
                    var area = bounds.Sum(rectangle => (double)rectangle.Width * rectangle.Height);
                    var rowWidth = Math.Max(widest,
                        Math.Min(Math.Max(1, maximumRowWidth), Math.Ceiling(Math.Sqrt(area * 1.6))));
                    long rowX = 0, rowY = 0, rowHeight = 0;
                    foreach (var index in Enumerable.Range(0, bounds.Count)
                                 .OrderByDescending(index => bounds[index].Height))
                    {
                        var rectangle = bounds[index];
                        if (rowX > 0 && rowX + rectangle.Width > rowWidth)
                        {
                            rowX = 0;
                            rowY += rowHeight;
                            rowHeight = 0;
                        }

                        positions[index] = Point(left + rowX, top + rowY);
                        rowX += rectangle.Width;
                        rowHeight = Math.Max(rowHeight, rectangle.Height);
                    }

                    break;
                default: throw new ArgumentOutOfRangeException(nameof(arrangement));
            }

            return positions;
        }

        private static PixelPoint Point(long x, long y)
        {
            if (x is < int.MinValue or > int.MaxValue || y is < int.MinValue or > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(x));
            return new((int)x, (int)y);
        }
    }
}

using System.Buffers.Binary;

namespace RitsukageGif.Desktop
{
    internal static class PinnedInputRegionBuilder
    {
        internal static PinnedInputRegion Build(PinnedInputCoverage ui, ReadOnlySpan<byte> viewport,
            PixelRect viewportBounds, bool viewportOpaque = false, bool preserveAdditiveColor = false,
            CancellationToken token = default)
        {
            var result = new List<PixelRect>();
            var previous = new List<PixelRect>();
            var current = new List<PixelRect>();
            for (var y = 0; y < ui.Size.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                current.Clear();
                var oldIndex = 0;
                var x = 0;
                while (x < ui.Size.Width)
                {
                    if (!Covered(ui, viewport, viewportBounds, x, y, viewportOpaque, preserveAdditiveColor))
                    {
                        x++;
                        continue;
                    }

                    var left = x++;
                    while (x < ui.Size.Width && Covered(ui, viewport, viewportBounds, x, y, viewportOpaque,
                               preserveAdditiveColor)) x++;
                    while (oldIndex < previous.Count && previous[oldIndex].X < left)
                        result.Add(previous[oldIndex++]);
                    if (oldIndex < previous.Count && previous[oldIndex].X == left &&
                        previous[oldIndex].Width == x - left)
                    {
                        var rectangle = previous[oldIndex++];
                        current.Add(new(left, rectangle.Y, rectangle.Width, rectangle.Height + 1));
                    }
                    else
                    {
                        current.Add(new(left, y, x - left, 1));
                    }
                }

                while (oldIndex < previous.Count) result.Add(previous[oldIndex++]);
                (previous, current) = (current, previous);
            }

            result.AddRange(previous);
            result.Sort(static (left, right) =>
                left.Y != right.Y ? left.Y.CompareTo(right.Y) : left.X.CompareTo(right.X));
            return new(ui.Size, [.. result]);
        }

        private static bool Covered(PinnedInputCoverage ui, ReadOnlySpan<byte> viewport, PixelRect bounds,
            int x, int y, bool viewportOpaque, bool preserveAdditiveColor)
        {
            if (ui.Contains(x, y)) return true;
            if (viewport.IsEmpty || x < bounds.X || y < bounds.Y || x >= bounds.Right || y >= bounds.Bottom)
                return false;
            if (viewportOpaque) return true;
            var pixel = checked(((y - bounds.Y) * bounds.Width + x - bounds.X) * 8);
            if (BinaryPrimitives.ReadHalfLittleEndian(viewport[(pixel + 6)..]) > (Half)0) return true;
            return preserveAdditiveColor &&
                   (BinaryPrimitives.ReadHalfLittleEndian(viewport[pixel..]) != (Half)0 ||
                    BinaryPrimitives.ReadHalfLittleEndian(viewport[(pixel + 2)..]) != (Half)0 ||
                    BinaryPrimitives.ReadHalfLittleEndian(viewport[(pixel + 4)..]) != (Half)0);
        }
    }
}

using Avalonia;

namespace RitsukageGif.Desktop
{
    internal readonly record struct PinnedViewTransform(PinnedViewOrientation Orientation)
    {
        internal bool SwapsAxes => ((int)Orientation & 1) != 0;
        internal bool IsIdentity => Orientation == PinnedViewOrientation.Normal;

        internal Size ViewSize(Size source)
        {
            return SwapsAxes ? new(source.Height, source.Width) : source;
        }

        internal PinnedViewTransform Apply(PinnedImageTransform operation)
        {
            var rotation = (int)Orientation & 3;
            var mirror = (int)Orientation & 4;
            return operation switch
            {
                PinnedImageTransform.RotateClockwise =>
                    new((PinnedViewOrientation)(mirror | ((rotation + (mirror == 0 ? 1 : 3)) & 3))),
                PinnedImageTransform.RotateCounterclockwise =>
                    new((PinnedViewOrientation)(mirror | ((rotation + (mirror == 0 ? 3 : 1)) & 3))),
                PinnedImageTransform.FlipHorizontal =>
                    new((PinnedViewOrientation)((mirror ^ 4) | ((4 - rotation) & 3))),
                PinnedImageTransform.FlipVertical => new((PinnedViewOrientation)((mirror ^ 4) | ((6 - rotation) & 3))),
                PinnedImageTransform.Grayscale or PinnedImageTransform.Invert =>
                    throw new ArgumentOutOfRangeException(nameof(operation)),
                _ => throw new ArgumentOutOfRangeException(nameof(operation)),
            };
        }

        internal Matrix MatrixFor(Size source)
        {
            return Orientation switch
            {
                PinnedViewOrientation.Normal => Matrix.Identity,
                PinnedViewOrientation.Clockwise90 => new(0, 1, -1, 0, source.Height, 0),
                PinnedViewOrientation.Clockwise180 => new(-1, 0, 0, -1, source.Width, source.Height),
                PinnedViewOrientation.Clockwise270 => new(0, -1, 1, 0, 0, source.Width),
                PinnedViewOrientation.MirrorHorizontal => new(-1, 0, 0, 1, source.Width, 0),
                PinnedViewOrientation.MirrorHorizontalClockwise90 => new(0, -1, -1, 0, source.Height, source.Width),
                PinnedViewOrientation.MirrorHorizontalClockwise180 => new(1, 0, 0, -1, 0, source.Height),
                PinnedViewOrientation.MirrorHorizontalClockwise270 => new(0, 1, 1, 0, 0, 0),
                _ => throw new InvalidOperationException("Invalid pinned image orientation."),
            };
        }

        internal Rect SourceBounds(Rect visible, Size source)
        {
            return visible.TransformToAABB(MatrixFor(source).Invert()).Intersect(new(source));
        }

        internal (int X, int Y) PixelPosition(int x, int y, int width, int height)
        {
            if (((int)Orientation & 4) != 0) x = width - 1 - x;
            return ((int)Orientation & 3) switch
            {
                0 => (x, y),
                1 => (height - 1 - y, x),
                2 => (width - 1 - x, height - 1 - y),
                _ => (y, width - 1 - x),
            };
        }
    }
}

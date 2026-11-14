using Avalonia;

namespace RitsukageGif.Desktop
{
    internal static class SelectionRegionInfoLayout
    {
        internal static Rect Place(Size viewport, Rect selected, Size size, Rect toolbar, Rect options, Rect side,
            Rect modes, double nearOffset, double farOffset)
        {
            const double obstacleGap = 14;
            Span<Point> anchors =
            [
                new(selected.Left, selected.Top - size.Height - nearOffset),
                new(selected.Left - size.Width - nearOffset, selected.Top),
                new(selected.Right + farOffset, selected.Top),
                new(selected.Left, selected.Bottom + farOffset),
                selected.TopLeft,
                new(selected.Right - size.Width, selected.Top),
                new(selected.Left, selected.Bottom - size.Height),
                new(selected.Right - size.Width, selected.Bottom - size.Height),
                new(8, 8),
                new(viewport.Width - size.Width - 8, 8),
                new(8, viewport.Height - size.Height - 8),
                new(viewport.Width - size.Width - 8, viewport.Height - size.Height - 8),
            ];
            var best = default(Rect);
            var bestScore = double.PositiveInfinity;
            foreach (var anchor in anchors)
            {
                var frame = new Rect(Math.Clamp(anchor.X, 0, Math.Max(0, viewport.Width - size.Width)),
                    Math.Clamp(anchor.Y, 0, Math.Max(0, viewport.Height - size.Height)),
                    Math.Min(size.Width, viewport.Width), Math.Min(size.Height, viewport.Height));
                var blocked = Area(frame.Inflate(4), toolbar) + Area(frame.Inflate(4), options) +
                              Area(frame.Inflate(4), side) + Area(frame.Inflate(4), modes);
                var fit = frame.Position == anchor;
                if (fit && blocked <= 0) return frame;
                var score = blocked * 1e6 + Math.Abs(frame.X - anchor.X) + Math.Abs(frame.Y - anchor.Y);
                if (score >= bestScore) continue;
                bestScore = score;
                best = frame;
            }

            Span<Rect> obstacles = [toolbar, options, side, modes];
            Span<double> horizontal = stackalloc double[12];
            Span<double> vertical = stackalloc double[12];
            horizontal[0] = selected.Left;
            horizontal[1] = selected.Right - size.Width;
            horizontal[2] = 8;
            horizontal[3] = viewport.Width - size.Width - 8;
            vertical[0] = selected.Top;
            vertical[1] = selected.Bottom - size.Height;
            vertical[2] = 8;
            vertical[3] = viewport.Height - size.Height - 8;
            var count = 4;
            foreach (var obstacle in obstacles)
            {
                if (obstacle.Width <= 0 || obstacle.Height <= 0) continue;
                horizontal[count] = obstacle.Left - size.Width - obstacleGap;
                vertical[count++] = obstacle.Top - size.Height - obstacleGap;
                horizontal[count] = obstacle.Right + obstacleGap;
                vertical[count++] = obstacle.Bottom + obstacleGap;
            }

            foreach (var x in horizontal[..count])
            foreach (var y in vertical[..count])
            {
                var frame = new Rect(Math.Clamp(x, 0, Math.Max(0, viewport.Width - size.Width)),
                    Math.Clamp(y, 0, Math.Max(0, viewport.Height - size.Height)),
                    Math.Min(size.Width, viewport.Width), Math.Min(size.Height, viewport.Height));
                var blocked = false;
                foreach (var obstacle in obstacles)
                    if (Area(frame.Inflate(4), obstacle) > 0)
                    {
                        blocked = true;
                        break;
                    }

                if (!blocked) return frame;
            }

            return best;
        }

        private static double Area(Rect first, Rect second)
        {
            var intersection = first.Intersect(second);
            return intersection.Width * intersection.Height;
        }
    }
}

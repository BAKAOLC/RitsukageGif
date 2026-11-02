using Avalonia;

namespace RitsukageGif.Desktop
{
    internal static class SelectionFrameLayout
    {
        internal static Rect Place(Size viewport, Rect selection, Size size, Rect toolbar, Rect options)
        {
            const double gap = 10;
            Span<Point> anchors =
            [
                new(selection.Right + gap, selection.Top),
                new(selection.Left - size.Width - gap, selection.Top),
                new(selection.Right + gap, selection.Bottom - size.Height),
                new(selection.Left - size.Width - gap, selection.Bottom - size.Height),
                new(selection.Right - size.Width - gap, selection.Top + gap),
                new(selection.Left + gap, selection.Top + gap),
            ];
            var best = default(Rect);
            var bestScore = (Selection: double.PositiveInfinity, Obstacle: double.PositiveInfinity,
                Distance: double.PositiveInfinity);
            foreach (var anchor in anchors)
            {
                var frame = Consider(anchor);
                if (Clear(frame)) return frame;
            }

            Span<double> columns =
            [
                selection.Right + gap, selection.Left - size.Width - gap,
                toolbar.Right + gap, toolbar.Left - size.Width - gap,
                options.Right + gap, options.Left - size.Width - gap,
                4, viewport.Width - size.Width - 4,
            ];
            Span<double> rows =
            [
                selection.Top, selection.Bottom - size.Height,
                selection.Top - size.Height - gap, selection.Bottom + gap,
                toolbar.Bottom + gap, toolbar.Top - size.Height - gap,
                options.Bottom + gap, options.Top - size.Height - gap,
                4, viewport.Height - size.Height - 4,
            ];
            foreach (var x in columns)
            foreach (var y in rows)
                Consider(new(x, y));
            return best;

            bool Clear(Rect frame)
            {
                return Overlap(frame, selection) <= 0 && Overlap(frame.Inflate(4), toolbar) <= 0 &&
                       Overlap(frame.Inflate(4), options) <= 0;
            }

            Rect Consider(Point anchor)
            {
                var frame = new Rect(Math.Clamp(anchor.X, 4, Math.Max(4, viewport.Width - size.Width - 4)),
                    Math.Clamp(anchor.Y, 4, Math.Max(4, viewport.Height - size.Height - 4)),
                    Math.Min(size.Width, Math.Max(0, viewport.Width - 8)),
                    Math.Min(size.Height, Math.Max(0, viewport.Height - 8)));
                var score = (Overlap(frame, selection),
                    Overlap(frame.Inflate(4), toolbar) + Overlap(frame.Inflate(4), options),
                    Math.Abs(frame.X - selection.Right - gap) + Math.Abs(frame.Y - selection.Top));
                if (score.CompareTo(bestScore) < 0)
                {
                    best = frame;
                    bestScore = score;
                }

                return frame;
            }
        }

        private static double Overlap(Rect first, Rect second)
        {
            var overlap = first.Intersect(second);
            return overlap.Width * overlap.Height;
        }
    }
}

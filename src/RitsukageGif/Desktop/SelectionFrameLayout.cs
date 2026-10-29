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
            var bestScore = double.PositiveInfinity;
            foreach (var anchor in anchors)
            {
                var frame = new Rect(Math.Clamp(anchor.X, 4, Math.Max(4, viewport.Width - size.Width - 4)),
                    Math.Clamp(anchor.Y, 4, Math.Max(4, viewport.Height - size.Height - 4)),
                    Math.Min(size.Width, Math.Max(0, viewport.Width - 8)),
                    Math.Min(size.Height, Math.Max(0, viewport.Height - 8)));
                var score = (Overlap(frame.Inflate(4), toolbar) + Overlap(frame.Inflate(4), options)) * 1e8 +
                            Overlap(frame, selection) * 1e3 +
                            Math.Abs(frame.X - anchor.X) + Math.Abs(frame.Y - anchor.Y);
                if (score >= bestScore) continue;
                best = frame;
                bestScore = score;
            }

            return best;
        }

        private static double Overlap(Rect first, Rect second)
        {
            var overlap = first.Intersect(second);
            return overlap.Width * overlap.Height;
        }
    }
}

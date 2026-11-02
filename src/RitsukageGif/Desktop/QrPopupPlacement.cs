using Avalonia;

namespace RitsukageGif.Desktop
{
    internal static class QrPopupPlacement
    {
        internal static Rect Place(Rect symbol, Size requested, Size viewport, IReadOnlyList<Rect> reserved)
        {
            const double gap = 8;
            var size = new Size(Math.Min(requested.Width, Math.Max(0, viewport.Width - gap * 2)),
                Math.Min(requested.Height, Math.Max(0, viewport.Height - gap * 2)));
            Point[] anchors =
            [
                new(symbol.Right + gap, symbol.Top), new(symbol.Left, symbol.Bottom + gap),
                new(symbol.Left - size.Width - gap, symbol.Top), new(symbol.Left, symbol.Top - size.Height - gap),
                new(gap, gap), new(viewport.Width - size.Width - gap, gap),
                new(gap, viewport.Height - size.Height - gap),
                new(viewport.Width - size.Width - gap, viewport.Height - size.Height - gap),
            ];
            var best = default(Rect);
            var bestScore = double.PositiveInfinity;
            foreach (var anchor in anchors)
            {
                var candidate = new Rect(Math.Clamp(anchor.X, gap, Math.Max(gap, viewport.Width - size.Width - gap)),
                    Math.Clamp(anchor.Y, gap, Math.Max(gap, viewport.Height - size.Height - gap)), size.Width,
                    size.Height);
                var intersection = candidate.Intersect(symbol);
                var score = intersection.Width * intersection.Height;
                foreach (var occupied in reserved)
                {
                    intersection = candidate.Inflate(4).Intersect(occupied);
                    score += intersection.Width * intersection.Height * 8;
                }

                if (score >= bestScore) continue;
                best = candidate;
                bestScore = score;
            }

            return best;
        }
    }
}

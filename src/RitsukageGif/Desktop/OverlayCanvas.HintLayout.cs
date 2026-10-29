using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayCanvas
    {
        private static Rect FitPanel(Size viewport, Size size, Point position)
        {
            var width = Math.Min(size.Width, Math.Max(0, viewport.Width - HintMargin * 2));
            var height = Math.Min(size.Height, Math.Max(0, viewport.Height - HintMargin * 2));
            var left = Math.Min(HintMargin, Math.Max(0, viewport.Width - width));
            var top = Math.Min(HintMargin, Math.Max(0, viewport.Height - height));
            return new(Math.Clamp(position.X, left, Math.Max(left, viewport.Width - HintMargin - width)),
                Math.Clamp(position.Y, top, Math.Max(top, viewport.Height - HintMargin - height)), width, height);
        }

        private static double Overlap(Rect frame, Rect avoid)
        {
            var overlap = frame.Intersect(avoid);
            return overlap.Width * overlap.Height;
        }

        private static double HandleOverlap(Rect frame, Rect selected)
        {
            if (selected.Width <= 0 || selected.Height <= 0) return 0;
            Span<Point> handles =
            [
                selected.TopLeft, new(selected.Center.X, selected.Top), selected.TopRight,
                new(selected.Right, selected.Center.Y), selected.BottomRight,
                new(selected.Center.X, selected.Bottom), selected.BottomLeft, new(selected.Left, selected.Center.Y),
            ];
            var area = 0.0;
            foreach (var point in handles)
                area += Overlap(frame, new(point.X - 10, point.Y - 10, 20, 20));
            return area;
        }

        private static bool PanelClear(Rect frame, Point mouse, Rect selected, Rect first = default,
            Rect second = default, Rect third = default, Rect fourth = default, Rect fifth = default)
        {
            return !frame.Inflate(18).Contains(mouse) && HandleOverlap(frame, selected) == 0 &&
                   Overlap(frame.Inflate(4), first) == 0 && Overlap(frame.Inflate(4), second) == 0 &&
                   Overlap(frame.Inflate(4), third) == 0 && Overlap(frame.Inflate(4), fourth) == 0 &&
                   Overlap(frame.Inflate(4), fifth) == 0;
        }

        private static Rect GetActionsFrame(Size viewport, Size size, Point mouse, Rect selected = default,
            Rect reserved = default, Rect secondaryReserved = default, Rect frameReserved = default)
        {
            var x = (viewport.Width - size.Width) / 2;
            const double edgeGap = 20;
            Span<Point> anchors =
            [
                new(HintMargin, viewport.Height - edgeGap - size.Height), new(HintMargin, edgeGap),
                new(x, viewport.Height - edgeGap - size.Height), new(x, edgeGap),
                new(viewport.Width - size.Width - HintMargin, viewport.Height - edgeGap - size.Height),
                new(viewport.Width - size.Width - HintMargin, edgeGap),
            ];
            var best = default(Rect);
            var bestScore = double.PositiveInfinity;
            foreach (var anchor in anchors)
            {
                var frame = FitPanel(viewport, size, anchor);
                var score = (frame.Inflate(18).Contains(mouse) ? 1e12 : 0) +
                            (Overlap(frame.Inflate(4), reserved) + Overlap(frame.Inflate(4), secondaryReserved) +
                             Overlap(frame.Inflate(4), frameReserved) +
                             HandleOverlap(frame, selected)) * 1e6 +
                            Overlap(frame, selected);
                if (score >= bestScore) continue;
                bestScore = score;
                best = frame;
            }

            return best;
        }

        private static Rect GetStatusFrame(Size viewport, Size size, Rect selected, Point mouse, Rect actions = default,
            Rect reserved = default, Rect secondaryReserved = default, Rect frameReserved = default)
        {
            if (selected.Width <= 0 || selected.Height <= 0)
                return GetHintFrame(viewport, size, mouse, default, actions, default, reserved, secondaryReserved,
                    frameReserved);
            const double gap = 14;
            Span<Point> anchors =
            [
                new(selected.Left, selected.Top - size.Height - gap), new(selected.Left, selected.Bottom + gap),
                new(selected.Right - size.Width, selected.Top - size.Height - gap),
                new(selected.Right - size.Width, selected.Bottom + gap),
                new(selected.Left - size.Width - gap, selected.Top), new(selected.Right + gap, selected.Top),
                new(selected.Left + gap, selected.Top + gap),
                new(selected.Right - size.Width - gap, selected.Bottom - size.Height - gap),
            ];
            var best = default(Rect);
            var bestScore = double.PositiveInfinity;
            foreach (var anchor in anchors)
            {
                var frame = FitPanel(viewport, size, anchor);
                var score = (frame.Inflate(18).Contains(mouse) ? 1e12 : 0) +
                            (Overlap(frame.Inflate(4), actions) + Overlap(frame.Inflate(4), reserved) +
                             Overlap(frame.Inflate(4), secondaryReserved) + Overlap(frame.Inflate(4), frameReserved) +
                             HandleOverlap(frame, selected)) * 1e6 +
                            Overlap(frame, selected) + Math.Abs(frame.X - anchor.X) + Math.Abs(frame.Y - anchor.Y);
                if (score >= bestScore) continue;
                bestScore = score;
                best = frame;
            }

            return best;
        }

        private static Rect GetHintFrame(Size viewport, Size size, Point mouse, Rect avoid = default,
            Rect status = default, Rect actions = default, Rect reserved = default, Rect secondaryReserved = default,
            Rect frameReserved = default)
        {
            const double gap = 26;
            Span<Point> candidates =
            [
                new(mouse.X + gap, mouse.Y + gap), new(mouse.X - size.Width - gap, mouse.Y + gap),
                new(mouse.X + gap, mouse.Y - size.Height - gap),
                new(mouse.X - size.Width - gap, mouse.Y - size.Height - gap),
                new(mouse.X + gap, mouse.Y - size.Height / 2),
                new(mouse.X - size.Width - gap, mouse.Y - size.Height / 2),
                new(mouse.X - size.Width / 2, mouse.Y + gap),
                new(mouse.X - size.Width / 2, mouse.Y - size.Height - gap),
                new(HintMargin, HintMargin), new(viewport.Width - size.Width - HintMargin, HintMargin),
                new(HintMargin, viewport.Height - size.Height - HintMargin),
                new(viewport.Width - size.Width - HintMargin, viewport.Height - size.Height - HintMargin),
            ];
            var best = default(Rect);
            var bestScore = double.PositiveInfinity;
            foreach (var point in candidates)
            {
                var frame = FitPanel(viewport, size, point);
                var delta = frame.Center - mouse;
                var score = (frame.Inflate(18).Contains(mouse) ? 1e12 : 0) +
                            (Overlap(frame.Inflate(4), status) + Overlap(frame.Inflate(4), actions) +
                             Overlap(frame.Inflate(4), reserved) + Overlap(frame.Inflate(4), secondaryReserved) +
                             Overlap(frame.Inflate(4), frameReserved) +
                             HandleOverlap(frame, avoid)) * 1e6 +
                            Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
                if (score >= bestScore) continue;
                bestScore = score;
                best = frame;
            }

            return best;
        }
    }
}

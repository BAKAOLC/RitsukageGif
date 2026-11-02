using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayCanvas
    {
        internal IReadOnlyList<Rect> CodeReservedBounds { get; set; } = [];

        private bool CodePanelsClear(Rect frame)
        {
            return CodeReservedBounds.All(panel => !frame.Inflate(4).Intersects(panel));
        }

        private Rect AvoidCodePanels(Rect preferred, Point mouse, Rect selected, Rect first = default,
            Rect second = default)
        {
            if (CodePanelsClear(preferred)) return preferred;
            var best = preferred;
            var score = double.PositiveInfinity;
            foreach (var panel in CodeReservedBounds)
            {
                Span<Point> positions =
                [
                    new(panel.Right + 8, preferred.Y), new(panel.Left - preferred.Width - 8, preferred.Y),
                    new(preferred.X, panel.Bottom + 8), new(preferred.X, panel.Top - preferred.Height - 8),
                    new(panel.Right + 8, panel.Bottom + 8), new(panel.Left - preferred.Width - 8, panel.Bottom + 8),
                    new(panel.Right + 8, panel.Top - preferred.Height - 8),
                    new(panel.Left - preferred.Width - 8, panel.Top - preferred.Height - 8),
                ];
                foreach (var position in positions)
                {
                    var candidate = FitPanel(Bounds.Size, preferred.Size, position);
                    if (!CodePanelsClear(candidate) ||
                        !PanelClear(candidate, mouse, selected, ReservedBounds, SecondaryReservedBounds,
                            FrameReservedBounds, ModePickerReservedBounds, InformationReservedBounds, first, second))
                        continue;
                    var offset = candidate.TopLeft - preferred.TopLeft;
                    var distance = offset.X * offset.X + offset.Y * offset.Y;
                    if (distance >= score) continue;
                    score = distance;
                    best = candidate;
                }
            }

            return best;
        }
    }
}

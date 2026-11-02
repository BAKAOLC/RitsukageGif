using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayCanvas
    {
        private static readonly IPen MeasurementPen = new Pen(Brushes.White, 1, DashStyle.Dash);
        private readonly List<Rect> _measurementLabels = [];

        private (PixelRect Region, PixelRect Reference, string Kind, bool Right, bool Bottom, string Culture, string
            Unit)
            _measurementKey;

        private IReadOnlyList<(SelectionMeasurement Line, FormattedText Text)> _measurements = [];
        internal PixelRect MeasurementControlBounds { get; set; }
        internal PixelRect MeasurementWindowBounds { get; set; }
        internal PixelRect MeasurementScreenBounds { get; set; }

        private void DrawMeasurements(DrawingContext context)
        {
            var reference = SelectionMeasurements.Reference(Selected,
                MeasurementScreenBounds.IsEmpty ? _monitor : MeasurementScreenBounds,
                MeasurementWindowBounds, MeasurementControlBounds, Mouse);
            var key = (Selected, reference.Bounds, reference.Kind, reference.Right, reference.Bottom,
                UiText.Get("MeasurementScreen"), CoordinateUnit);
            if (key != _measurementKey)
            {
                _measurementKey = key;
                _measurements =
                [
                    .. SelectionMeasurements.Create(Selected, reference.Bounds, reference.Kind,
                            reference.Right, reference.Bottom, CoordinateUnit)
                        .Select(line => (line, HintText(line.Label, 220, Brushes.White, 12, 20))),
                ];
            }

            _measurementLabels.Clear();
            foreach (var (line, text) in _measurements)
            {
                var start = new Point((line.Start.X - _monitor.X) / Scale, (line.Start.Y - _monitor.Y) / Scale);
                var end = new Point((line.End.X - _monitor.X) / Scale, (line.End.Y - _monitor.Y) / Scale);
                context.DrawLine(MeasurementPen, start, end);
                var midpoint = new PixelPoint(line.Start.X + (line.End.X - line.Start.X) / 2,
                    line.Start.Y + (line.End.Y - line.Start.Y) / 2);
                if (!_monitor.Contains(midpoint)) continue;
                var horizontal = Math.Abs(start.X - end.X) >= Math.Abs(start.Y - end.Y);
                var cap = horizontal ? new Vector(0, 3) : new Vector(3, 0);
                context.DrawLine(MeasurementPen, start - cap, start + cap);
                context.DrawLine(MeasurementPen, end - cap, end + cap);
                var width = Math.Min(text.Width + 8, Bounds.Width);
                var height = text.Height + 4;
                var center = new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2);
                var x = Math.Clamp(center.X - width / 2 + (horizontal ? 0 : width / 2 + 5), 0,
                    Math.Max(0, Bounds.Width - width));
                var y = Math.Clamp(center.Y - height - 3, 0, Math.Max(0, Bounds.Height - height));
                for (var attempt = 0; attempt < 18; attempt++)
                {
                    var step = attempt / 2;
                    var offset = (step + 1) / 2 * (step % 2 == 0 ? 1 : -1);
                    var opposite = attempt % 2 != 0;
                    var originX = !horizontal && opposite ? center.X - width - 5 : x;
                    var originY = horizontal && opposite ? center.Y + 3 : y;
                    var label = new Rect(
                        Math.Clamp(originX + (horizontal ? offset * 36 : 0), 0, Math.Max(0, Bounds.Width - width)),
                        Math.Clamp(originY + (horizontal ? 0 : offset * 22), 0, Math.Max(0, Bounds.Height - height)),
                        width, height);
                    if (label.Intersects(ReservedBounds) || label.Intersects(SecondaryReservedBounds) ||
                        label.Intersects(FrameReservedBounds) || label.Intersects(ModePickerReservedBounds) ||
                        label.Intersects(InformationReservedBounds) || !CodePanelsClear(label) ||
                        MeasurementHintOverlaps(label, _hintStatusBounds) ||
                        MeasurementHintOverlaps(label, _hintActionsBounds) ||
                        MeasurementHintOverlaps(label, _hintMagnifierBounds) ||
                        MeasurementLabelOverlaps(label)) continue;
                    _measurementLabels.Add(label);
                    context.DrawRectangle(HintBackground, HintBorder, label, 3, 3);
                    context.DrawText(text, label.TopLeft + new Vector(4, 2));
                    break;
                }
            }
        }

        private static bool MeasurementHintOverlaps(Rect label, Rect hint)
        {
            return hint is { Width: > 0, Height: > 0 } && label.Intersects(hint.Inflate(4));
        }

        private bool MeasurementLabelOverlaps(Rect label)
        {
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
            foreach (var occupied in _measurementLabels)
                if (label.Intersects(occupied))
                    return true;
            return false;
        }
    }
}

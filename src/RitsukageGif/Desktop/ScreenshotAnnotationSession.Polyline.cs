using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private const double PolylinePointDistance = 2;

        public void BeginWithModifiers(PixelPoint point, bool shift)
        {
            _shiftPressed = shift;
            Begin(point);
        }

        public void MovePolylinePreview(PixelPoint point, bool shift)
        {
            if (_gesture is not { Tool: ScreenshotTool.Polyline } polyline) return;
            _shiftPressed = shift;
            var current = PolylinePoint(polyline, point);
            if (polyline.Points[^1] == current) return;
            polyline.Points[^1] = current;
            Changed?.Invoke();
        }

        private Point PolylinePoint(ScreenshotAnnotation polyline, PixelPoint point)
        {
            return ConstrainSegmentPoint(polyline.Points[^2], point, _shiftPressed);
        }

        private void AppendPolylinePoint(ScreenshotAnnotation polyline, PixelPoint point)
        {
            var current = PolylinePoint(polyline, point);
            polyline.Points[^1] = current;
            if (Distance(polyline.Points[^2], current) > PolylinePointDistance)
                polyline.Points.Add(current);
            Changed?.Invoke();
        }

        private void EndPolylineStroke(ScreenshotAnnotation polyline)
        {
            if (polyline.Points.Count != 2 ||
                Distance(polyline.Points[0], polyline.Points[1]) <= PolylinePointDistance) return;
            polyline.Points.Add(polyline.Points[^1]);
            FinishPolyline();
        }
    }
}

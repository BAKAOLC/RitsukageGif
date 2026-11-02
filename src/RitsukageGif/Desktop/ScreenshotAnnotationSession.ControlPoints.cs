using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private static int ObjectControlPointCount(ScreenshotAnnotation annotation)
        {
            return annotation.Tool is ScreenshotTool.Arrow or ScreenshotTool.Line || IsMarkerLine(annotation) ? 2 : 4;
        }

        private static Point ObjectControlPoint(ScreenshotAnnotation annotation, int index)
        {
            if (annotation.Tool is ScreenshotTool.Arrow or ScreenshotTool.Line || IsMarkerLine(annotation))
                return index == 0 ? annotation.Points[0] : annotation.Points[^1];
            return index switch
            {
                0 => annotation.Bounds.TopLeft,
                1 => annotation.Bounds.TopRight,
                2 => annotation.Bounds.BottomRight,
                _ => annotation.Bounds.BottomLeft,
            };
        }

        private bool HitTestObjectControl(ScreenshotAnnotation annotation, Point location)
        {
            for (var index = 0; index < ObjectControlPointCount(annotation); index++)
                if (Distance(ObjectControlPoint(annotation, index), location) <= ControlHitRadius)
                    return true;
            return false;
        }

        private void MoveLineEndpoint(ScreenshotAnnotation original, PixelPoint point)
        {
            var replacement = ReferenceEquals(_selected, original) ? CloneAnnotation(original) : _selected!;
            var index = _annotations.IndexOf(_selected!);
            var endpoint = _resizeCorner == 0 ? 0 : replacement.Points.Count - 1;
            var anchor = original.Points[_resizeCorner == 0 ? original.Points.Count - 1 : 0];
            replacement.Points[endpoint] = ConstrainSegmentPoint(anchor, point, _shiftPressed);
            _annotations[index] = replacement;
            _selected = replacement;
            Changed?.Invoke();
        }
    }
}

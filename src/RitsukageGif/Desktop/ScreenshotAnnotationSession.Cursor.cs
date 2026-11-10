using Avalonia;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        public StandardCursorType PointerCursor
        {
            get
            {
                if (!CanAnnotate || IsBusy) return StandardCursorType.Arrow;
                if (IsPrivacyDetectionActive) return CreationCursor();
                if (_moving is { } moving)
                {
                    if (moving.Tool == ScreenshotTool.Number && _polylineNode == 1)
                        return StandardCursorType.SizeNorthSouth;
                    if (_resizeCorner >= 0) return AnnotationControlCursor(moving, _resizeCorner);
                    return _polylineNode >= 0 || _resizeCorner == -2
                        ? StandardCursorType.Cross
                        : StandardCursorType.SizeAll;
                }

                if (IsDrawing || _hoverPoint is not { } pointer || IsCreationOnlyTool())
                    return CreationCursor();
                var candidate = FindMoveAnnotation(pointer, true);
                if (candidate is null) return CreationCursor();
                var point = new Point(pointer.X, pointer.Y);
                if (CanEditNumberTarget(candidate) && Distance(candidate.Points[1], point) <= ControlHitRadius)
                    return StandardCursorType.SizeNorthSouth;
                if (CanEditControlPoints(candidate))
                    for (var i = 0; i < ObjectControlPointCount(candidate); i++)
                        if (Distance(ObjectControlPoint(candidate, i), point) <= ControlHitRadius)
                            return AnnotationControlCursor(candidate, i);
                if ((CanEditPolylineNodes(candidate) &&
                     candidate.Points.Any(node => Distance(node, point) <= ControlHitRadius)) ||
                    (candidate.Tool is ScreenshotTool.Arrow or ScreenshotTool.Line && candidate.Properties.Curved &&
                     _behavior.ArrowCurveControlPoints &&
                     Distance(ArrowControlPoint(candidate), point) <= ControlHitRadius))
                    return StandardCursorType.Cross;
                return StandardCursorType.SizeAll;
            }
        }

        private bool IsCreationOnlyTool()
        {
            return Tool is ScreenshotTool.Brush or ScreenshotTool.StrokeEraser ||
                   (Tool == ScreenshotTool.Eraser && Properties.PixelPath == AnnotationPixelPath.Brush);
        }

        private StandardCursorType CreationCursor()
        {
            if (Tool is ScreenshotTool.Select or ScreenshotTool.Number) return StandardCursorType.Arrow;
            return Tool == ScreenshotTool.Brush ||
                   (Tool == ScreenshotTool.Highlight && Properties.MarkerPath == AnnotationMarkerPath.Line) ||
                   (IsPixelTool(Tool) && Properties.PixelPath == AnnotationPixelPath.Brush)
                ? StandardCursorType.None
                : StandardCursorType.Cross;
        }

        private static StandardCursorType AnnotationControlCursor(ScreenshotAnnotation annotation, int index)
        {
            if (annotation.Tool is ScreenshotTool.Arrow or ScreenshotTool.Line || IsMarkerLine(annotation))
                return StandardCursorType.Cross;
            return index % 2 == 0 ? StandardCursorType.TopLeftCorner : StandardCursorType.TopRightCorner;
        }

        private ScreenshotAnnotation? FindMoveAnnotation(PixelPoint point, bool currentToolOnly)
        {
            return HitTestSelectedControl(point) &&
                   (!currentToolOnly || Tool == ScreenshotTool.Select ||
                    (_selected is not null && ScreenshotToolCatalog.Get(AnnotationTool(_selected)).ObjectKind ==
                        ScreenshotToolCatalog.Get(Tool).ObjectKind))
                ? _selected
                : FindAnnotation(point, currentToolOnly);
        }
    }
}

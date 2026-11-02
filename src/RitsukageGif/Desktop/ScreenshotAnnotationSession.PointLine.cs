namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        public bool UsesPointLineCreation => Tool is ScreenshotTool.Arrow or ScreenshotTool.Line ||
                                             (Tool == ScreenshotTool.Highlight &&
                                              Properties.MarkerPath == AnnotationMarkerPath.Line);

        public bool IsDrawingPointLine => _gesture is { } annotation && IsPointLine(annotation);

        private static bool IsPointLine(ScreenshotAnnotation annotation)
        {
            return annotation.Tool is ScreenshotTool.Arrow or ScreenshotTool.Line || IsMarkerLine(annotation);
        }

        public void MovePointLinePreview(PixelPoint point, bool shift)
        {
            if (!IsDrawingPointLine) return;
            _shiftPressed = shift;
            Move(point);
        }

        public void ConfirmPointLine(PixelPoint point, bool shift)
        {
            if (!IsDrawingPointLine) return;
            MovePointLinePreview(point, shift);
            FinishPointLine();
        }

        private void FinishPointLine()
        {
            if (_gesture is not { } annotation || !IsPointLine(annotation) || annotation.Points.Count < 2 ||
                Distance(annotation.Points[0], annotation.Points[^1]) <= PolylinePointDistance) return;
            _gesture = null;
            Commit(annotation);
            Changed?.Invoke();
        }
    }
}

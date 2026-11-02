namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private bool? _secondaryEditingOverride;

        private bool SecondaryEditingEnabled => _secondaryEditingOverride ??
                                                _behavior.SecondaryEditing == AnnotationSecondaryEditingMode.Enabled;

        public bool AllowUnrestrictedSelection { get; init; } = true;

        public bool ToolbarVisible { get; private set; } = true;

        private bool CanEditAnnotation(ScreenshotAnnotation annotation, bool focused = false)
        {
            if (_behavior.SecondaryEditing == AnnotationSecondaryEditingMode.TextOnly)
                return annotation.Tool == ScreenshotTool.Text;
            if (AllowUnrestrictedSelection && Tool == ScreenshotTool.Select)
                return SecondaryEditingEnabled || focused;
            return annotation switch
            {
                { Tool: ScreenshotTool.Brush or ScreenshotTool.StrokeEraser } => false,
                {
                    Tool: ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.SmartErase,
                    Properties.PixelPath: AnnotationPixelPath.Brush,
                } => false,
                { Tool: ScreenshotTool.Eraser } when !_behavior.EraserSecondaryEditingEnabled => false,
                _ => SecondaryEditingEnabled || focused,
            };
        }

        public void ToggleSecondaryEditing()
        {
            if (IsBusy || IsDrawing || IsEditingText) return;
            _secondaryEditingOverride = !SecondaryEditingEnabled;
            Changed?.Invoke();
        }

        public void ToggleToolbar()
        {
            if (IsBusy || IsDrawing || IsEditingText) return;
            ToolbarVisible = !ToolbarVisible;
            Changed?.Invoke();
        }
    }
}

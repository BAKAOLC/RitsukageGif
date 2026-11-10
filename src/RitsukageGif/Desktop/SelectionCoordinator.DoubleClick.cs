namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        internal void ExecuteScreenshotDoubleClick(PixelPoint point)
        {
            ExecuteScreenshotClick(point, CapturePreferences.DoubleClickAction);
        }

        internal void ExecuteScreenshotMiddleClick(PixelPoint point)
        {
            ExecuteScreenshotClick(point, CapturePreferences.MiddleClickAction);
        }

        private void ExecuteScreenshotClick(PixelPoint point, CaptureClickAction action)
        {
            if (!ScreenshotMode || _finished || _screenshotCompleting ||
                action == CaptureClickAction.None ||
                Annotations is not { IsBusy: false, IsDrawing: false } annotations) return;
            _pointer = point;
            UpdatePoint(true);
            annotations.ClearAnnotationSelection();
            SelectionLogger.ZLogDebug($"Screenshot click action requested. Action={action.ToString()}");
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (action)
            {
                case CaptureClickAction.CopyOcrText:
                    RecognizeSelection(RecognitionMode.Text, true);
                    return;
                case CaptureClickAction.Copy:
                    annotations.RequestComplete(ScreenshotOutputAction.Copy);
                    return;
                case CaptureClickAction.QuickSave:
                    annotations.RequestComplete(ScreenshotOutputAction.QuickSave);
                    return;
                case CaptureClickAction.Save:
                    annotations.RequestComplete(ScreenshotOutputAction.Save);
                    return;
                case CaptureClickAction.Pin:
                    annotations.RequestComplete(ScreenshotOutputAction.Pin);
                    return;
                default:
                    throw new InvalidOperationException($"Unsupported screenshot click action: {action}.");
            }
        }
    }
}

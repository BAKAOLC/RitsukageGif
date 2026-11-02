namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        internal void ExecuteScreenshotDoubleClick(PixelPoint point)
        {
            if (!ScreenshotMode || _finished || _screenshotCompleting ||
                Annotations is not { IsBusy: false, IsDrawing: false } annotations) return;
            _pointer = point;
            annotations.ClearAnnotationSelection();
            var action = CapturePreferences.DoubleClickAction;
            SelectionLogger.ZLogDebug($"Screenshot double-click action requested. Action={action.ToString()}");
            switch (action)
            {
                case CaptureDoubleClickAction.None:
                    return;
                case CaptureDoubleClickAction.CopyOcrText:
                    RecognizeSelection(RecognitionMode.Text, true);
                    return;
                case CaptureDoubleClickAction.Copy:
                    annotations.RequestComplete(ScreenshotOutputAction.Copy);
                    return;
                case CaptureDoubleClickAction.QuickSave:
                    annotations.RequestComplete(ScreenshotOutputAction.QuickSave);
                    return;
                case CaptureDoubleClickAction.Save:
                    annotations.RequestComplete(ScreenshotOutputAction.Save);
                    return;
                case CaptureDoubleClickAction.Pin:
                    annotations.RequestComplete(ScreenshotOutputAction.Pin);
                    return;
                default:
                    throw new InvalidOperationException($"Unsupported screenshot double-click action: {action}.");
            }
        }
    }
}

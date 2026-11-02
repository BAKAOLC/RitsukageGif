namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private Task OpenScreenshotRecorderAsync(SelectionResult selection)
        {
            if (IsClosing || IsSelecting || IsBusy || IsConfiguring) return Task.CompletedTask;
            ValidateRegion(selection.Region, !DesktopCapture.UsesSourceSelection && !selection.WindowSource);
            DeactivatePreset();
            SetRegion(selection.Region, !DesktopCapture.UsesSourceSelection && !selection.WindowSource);
            _manualPreset = selection.CreatePreset();
            _captureHistory.RecordRegion(selection);
            Settings.CaptureSource = selection.WindowSource ? CaptureSourceKind.Window : CaptureSourceKind.Screen;
            SetCaptureCursor(selection.Cursor);
            RefreshDisplaySelection();
            HostLogger.ZLogInformation(
                $"Screenshot transferred to recording preparation. Region={selection.Region} WindowSource={selection.WindowSource} Cursor={selection.Cursor}");
            return OpenCaptureRecorderAsync();
        }
    }
}

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private ScreenshotRecordingWindow? _screenshotRecorder;
        private bool _screenshotRecordingPreparing;
        private bool _screenshotRecordingCountingDown;
        private bool HasStoppableScreenshotRecording => _screenshotRecordingCountingDown || _screenshotRecordingActive;
        private bool CanStopActiveRecording => !IsClosing && (IsRecording || _screenshotRecordingCountingDown);
        private RecordingAnnotationLayer? ScreenshotRecordingAnnotations => _screenshotRecorder?.Annotations;
        private RecordingInputTrackState? ScreenshotRecordingInputTracks => _screenshotRecorder?.InputTracks;
        private RecordingCameraSession? ScreenshotRecordingCameraSession => _screenshotRecorder?.CameraSession;

        internal void UpdateScreenshotRecordingPreparation(bool preparing, bool countingDown)
        {
            if (_screenshotRecordingPreparing == preparing && _screenshotRecordingCountingDown == countingDown) return;
            _screenshotRecordingPreparing = preparing;
            _screenshotRecordingCountingDown = countingDown;
            Notify();
        }

        private Task StopActiveRecordingAsync()
        {
            return HasStoppableScreenshotRecording && _screenshotRecorder is { } recorder
                ? recorder.StopRecordingAsync()
                : IsRecording
                    ? ToggleRecordingAsync()
                    : Task.CompletedTask;
        }

        internal void UpdateScreenshotRecordingPreferences(ScreenshotRecordingPreferences preferences)
        {
            var normalized = preferences.Normalize();
            if (IsClosing || Settings.ScreenshotRecording == normalized) return;
            Settings = Settings with { ScreenshotRecording = normalized };
            QueueSettingsSave(Settings);
        }

        private Task OpenScreenshotRecorderAsync(SelectionResult selection)
        {
            if (IsClosing || IsSelecting || IsBusy || IsConfiguring ||
                _screenshotRecorder is { CanAcceptSelection: false }) return Task.CompletedTask;
            ValidateRegion(selection.Region, !DesktopCapture.UsesSourceSelection && !selection.WindowSource);
            HostLogger.ZLogInformation(
                $"Screenshot transferred to recording preparation. Region={selection.Region} WindowSource={selection.WindowSource} Cursor={selection.Cursor}");
            return OpenScreenshotRecordingWindowAsync(selection);
        }

        private Task OpenScreenshotRecordingWindowAsync(SelectionResult selection)
        {
            if (IsClosing) return Task.CompletedTask;
            if (_screenshotRecorder is null)
            {
                _screenshotRecorder = new(this, selection, () => Settings.Annotation, PersistAnnotationPreferences);
                _screenshotRecorder.Closed += (_, _) => _screenshotRecorder = null;
            }
            else
            {
                _screenshotRecorder.UpdateSelection(selection);
            }

            WindowActivation.Show(_screenshotRecorder);
            return Task.CompletedTask;
        }

        private Task CloseScreenshotRecorderForShutdownAsync()
        {
            return _screenshotRecorder?.CloseForShutdownAsync() ?? Task.CompletedTask;
        }
    }
}

using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private RecordingControl? _recordingControl;

        internal bool IsQuickRecording => IsRecording && !_screenshotRecordingActive;
        internal bool IsQuickRecordingOperation => IsBusy && !_screenshotRecordingActive;

        internal bool IsRecordingPaused => IsRecording && _recordingControl?.IsPaused == true;
        internal bool CanPauseRecording => IsRecording && _recordingControl?.CanPause == true;

        internal void PauseOwnedRecording(Task<RecordingOutcome>? operation, bool paused)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!IsRecording || operation is null || !ReferenceEquals(operation, _operation)) return;
            if (paused) _recordingControl?.Pause();
            else _recordingControl?.Resume();
        }

        private void BeginRecordingControl(RecordingControl? control = null)
        {
            _recordingControl = control ?? new();
            _recordingControl.StateChanged += RecordingControlChanged;
        }

        private void CompleteRecordingControl()
        {
            if (_recordingControl is { } control) control.StateChanged -= RecordingControlChanged;
            _recordingControl = null;
        }

        private void RecordingControlChanged(object? sender, EventArgs args)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => RecordingControlChanged(sender, args));
                return;
            }

            if (!ReferenceEquals(sender, _recordingControl) || !IsRecording) return;
            if (_recordingControl?.IsPaused == true) _recordClock.Stop();
            else _recordClock.Start();
            Notify();
        }
    }
}

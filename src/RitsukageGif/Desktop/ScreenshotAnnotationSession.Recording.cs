namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        internal bool RecordingEnabled
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                Changed?.Invoke();
            }
        }

        internal bool CanStartRecording => RecordingEnabled && CanAnnotate && CanExport;
        internal event Action? RecordingRequested;

        internal void RequestRecording()
        {
            if (CanStartRecording) RecordingRequested?.Invoke();
        }
    }
}

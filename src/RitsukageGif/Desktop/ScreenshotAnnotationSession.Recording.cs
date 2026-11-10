namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        internal bool RecordingContext { get; init; }

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

        private bool CanUseDrawingTool(ScreenshotTool tool)
        {
            return !RecordingContext || ScreenshotToolCatalog.Get(ScreenshotToolCatalog.CanonicalTool(tool))
                .SupportsRecording;
        }

        internal event Action? RecordingRequested;

        internal void RequestRecording()
        {
            if (CanStartRecording) RecordingRequested?.Invoke();
        }
    }
}

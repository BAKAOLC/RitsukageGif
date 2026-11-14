namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        internal bool WindowPinEnabled
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                Changed?.Invoke();
            }
        }

        internal bool CanPinWindow => WindowPinEnabled && CanAnnotate && CanExport;
        internal event Action? WindowPinRequested;

        internal void RequestWindowPin()
        {
            if (!CanPinWindow) return;
            CommitTextEdit();
            WindowPinRequested?.Invoke();
        }
    }
}

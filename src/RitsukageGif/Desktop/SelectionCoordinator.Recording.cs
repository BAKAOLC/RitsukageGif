namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        internal bool RecordingRequested { get; private set; }
        internal bool AllowRecording { get; init; }

        private void RequestRecording()
        {
            if (_finished || _screenshotCompleting || ScreenshotPreview || !AllowRecording ||
                Annotations is not { CanStartRecording: true } || !Snapshot.CanConfirm) return;
            RecordingRequested = true;
            Confirm();
            Refresh();
        }
    }
}

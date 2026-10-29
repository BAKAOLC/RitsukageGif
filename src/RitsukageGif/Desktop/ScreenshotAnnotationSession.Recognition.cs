namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        internal event Action<RecognitionMode>? RecognitionRequested;

        internal void RequestRecognition(RecognitionMode mode)
        {
            if (CanExport && CanAnnotate && RecognitionRequested is not null)
            {
                Logger.ZLogInformation(
                    $"Annotation recognition requested. SessionId={_logSessionId} Mode={mode.ToString()} Width={Selection.Width} Height={Selection.Height}");
                RecognitionRequested.Invoke(mode);
            }
        }
    }
}

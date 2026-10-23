namespace RitsukageGif.Desktop
{
    internal static class RecordingProgressText
    {
        public static string Format(RecordingProgress progress) => UiText.Format("RecordingProgressDetail",
            progress.CapturedFps, progress.Encoded, progress.Captured, progress.ElapsedSeconds);
    }
}

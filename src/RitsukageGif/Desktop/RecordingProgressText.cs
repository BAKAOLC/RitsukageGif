namespace RitsukageGif.Desktop
{
    internal static class RecordingProgressText
    {
        public static string Format(RecordingProgress progress)
        {
            return UiText.Format("RecordingProgressDetail",
                progress.CapturedFps, progress.Encoded, progress.Captured, progress.ElapsedSeconds);
        }
    }
}

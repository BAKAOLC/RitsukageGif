namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        internal RecordingReviewOutputContext CreateRecordingReviewOutputContext()
        {
            return new(() => ScreenshotSaveForOutput, () => Settings.OutputDirectory,
                CopyFilesAsync, ShowOutputNotification);
        }
    }
}

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        internal RecordingReviewOutputContext CreateRecordingReviewOutputContext()
        {
            return new(() => Settings.ScreenshotSave, () => Settings.OutputDirectory,
                CopyFilesAsync, ShowOutputNotification);
        }
    }
}

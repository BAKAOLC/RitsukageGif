namespace RitsukageGif.Desktop
{
    internal sealed record RecordingReviewOutputContext(
        Func<ScreenshotSavePreferences> Preferences,
        Func<string> FallbackDirectory,
        Func<IReadOnlyList<string>, Task> CopyFiles,
        Action<IReadOnlyList<string>> Notify);
}

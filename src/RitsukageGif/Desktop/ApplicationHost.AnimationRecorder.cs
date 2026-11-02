namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        internal Task<RecordingOutcome> StartAnimationRecordingAsync(RecordingOptions options, string directory)
        {
            if (IsClosing || IsBusy || IsSelecting || IsConfiguring || Region is not { } region)
                throw new InvalidOperationException(UiText.Get("CommandNotReady"));
            options.Validate();
            var previous = Settings;
            var updated = previous with
            {
                Fps = options.Fps, Divisor = options.Divisor, Format = options.Format,
                Quality = options.Quality, Cursor = options.Cursor, OutputDirectory = directory,
            };
            QueueSettingsSave(updated);
            Settings = updated;
            View?.RefreshSettingsPresentation(previous, updated);
            return StartAnimationRecordingCore(region);
        }
    }
}

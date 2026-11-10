namespace RitsukageGif.Desktop
{
    internal sealed class ScreenshotSoundPlayer(Func<string, Task>? play = null)
    {
        private static readonly ILogger SoundLogger = LogHub.GetLogger("Desktop.ScreenshotSound");
        private readonly Func<string, Task> _play = play ?? PlatformShell.PlaySoundAsync;

        internal void Play()
        {
            _ = PlayAsync();
        }

        private async Task PlayAsync()
        {
            try
            {
                await _play(Path.Combine(AppContext.BaseDirectory, "sounds", "screenshot.wav"));
            }
            catch (Exception error)
            {
                SoundLogger.ZLogWarning(error, $"Screenshot sound playback failed.");
            }
        }
    }
}

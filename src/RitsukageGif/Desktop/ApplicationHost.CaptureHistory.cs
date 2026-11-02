namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private Task _captureHistoryConfiguration = Task.CompletedTask;

        private void ConfigureCaptureHistoryBudget()
        {
            _captureHistory.Configure(Settings.CapturePreferences.RegionHistoryCapacity,
                Settings.CapturePreferences.ScreenshotHistoryCapacity,
                (long)Settings.CapturePreferences.ScreenshotHistoryMaximumMiB * 1024 * 1024);
        }

        private void ConfigureCaptureHistoryPersistence()
        {
            ConfigureCaptureHistoryBudget();
            _captureHistoryConfiguration = ConfigureCaptureHistoryPersistenceAsync(_captureHistoryConfiguration,
                Settings.CapturePreferences.PersistHistory);
        }

        private async Task ConfigureCaptureHistoryPersistenceAsync(Task previous, bool enabled)
        {
            await previous;
            try
            {
                await _captureHistory.ConfigurePersistenceAsync(
                    Path.Combine(ApplicationStorage.DataDirectory, "capture-history"), enabled);
            }
            catch (Exception error)
            {
                HostLogger.ZLogError(error, $"Capture history configuration failed. Enabled={enabled}");
            }
        }

        private Task WaitForCaptureHistoryAsync(CancellationToken token)
        {
            return _captureHistoryConfiguration.WaitAsync(token);
        }

        private async Task StopCaptureHistoryAsync()
        {
            await _captureHistoryConfiguration;
            await _captureHistory.DisposeAsync();
        }
    }
}

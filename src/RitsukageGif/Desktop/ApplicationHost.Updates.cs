namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private static readonly ILogger UpdateLogger = LogHub.GetLogger("Desktop.Updates");
        private readonly CancellationTokenSource _updateCancellation = new();
        private bool _automaticUpdateChecked;
        private Task<UpdateRelease?>? _updateCheck;
        private UpdateChannel _updateCheckChannel;

        internal UpdateRelease? AvailableUpdate { get; private set; }

        internal Task<UpdateRelease?> CheckForUpdatesAsync(CancellationToken token = default)
        {
            if (IsClosing) return Task.FromCanceled<UpdateRelease?>(new(true));
            var channel = Settings.System.UpdateChannel;
            if (_updateCheck is not { IsCompleted: false } || _updateCheckChannel != channel)
            {
                _updateCheckChannel = channel;
                _updateCheck = CheckUpdateCoreAsync(channel);
            }

            return _updateCheck.WaitAsync(token);
        }

        private async Task<UpdateRelease?> CheckUpdateCoreAsync(UpdateChannel channel)
        {
            var release = await UpdateChecker.CheckChannelAsync(channel, _updateCancellation.Token);
            if (!IsClosing && Settings.System.UpdateChannel == channel)
            {
                AvailableUpdate = release;
                Notify();
                UpdateLogger.ZLogDebug($"Update availability state refreshed. UpdateAvailable={release is not null}");
            }
            else
            {
                UpdateLogger.ZLogDebug($"Update result discarded because the application state changed.");
            }

            return release;
        }

        private async Task CheckAutomaticUpdateAsync()
        {
            if (_automaticUpdateChecked || !_startServices || !Settings.CheckForUpdates) return;
            _automaticUpdateChecked = true;
            try
            {
                await CheckForUpdatesAsync();
            }
            catch (Exception)
            {
                // Automatic checks must not interrupt recording or display network errors.
            }
        }

        private async Task StopUpdateCheckAsync()
        {
            await _updateCancellation.CancelAsync();
            if (_updateCheck is { } pending)
                try
                {
                    await pending;
                }
                catch (Exception)
                {
                    // Shutdown observes failed or cancelled checks without reopening the UI.
                }

            _updateCancellation.Dispose();
        }
    }
}

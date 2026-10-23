namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private readonly CancellationTokenSource _updateCancellation = new();
        private Task<UpdateRelease?>? _updateCheck;
        private bool _automaticUpdateChecked;

        internal UpdateRelease? AvailableUpdate { get; private set; }

        internal Task<UpdateRelease?> CheckForUpdatesAsync(CancellationToken token = default)
        {
            if (_closing) return Task.FromCanceled<UpdateRelease?>(new CancellationToken(true));
            if (_updateCheck is not { IsCompleted: false }) _updateCheck = CheckUpdateCoreAsync();
            return _updateCheck.WaitAsync(token);
        }

        private async Task<UpdateRelease?> CheckUpdateCoreAsync()
        {
            var release = await UpdateChecker.CheckAsync(_updateCancellation.Token);
            if (!_closing)
            {
                AvailableUpdate = release;
                Notify();
            }

            return release;
        }

        private async Task CheckAutomaticUpdateAsync()
        {
            if (_automaticUpdateChecked || !_startServices || !_settings.CheckForUpdates) return;
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

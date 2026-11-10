using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private bool _persistedStartWithWindows;
        private bool _persistedStartupHidden;
        private SettingsPersistenceQueue<AppSettings>? _settingsPersistence;
        private bool _startupRegistrationUncertain;

        private void QueueSettingsSave(AppSettings settings)
        {
            if (IsTaskHost) return;
            _settingsPersistence ??= new(PersistSettingsAsync, error =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!IsClosing) SetStatus(error.Message);
                });
            });
            _settingsPersistence.RequestSave(settings.Snapshot());
        }

        private async Task PersistSettingsAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            await _networkPersistenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                PersistSettingsCore(settings with { Network = Volatile.Read(ref _committedNetwork) });
            }
            finally
            {
                _networkPersistenceGate.Release();
            }
        }

        private void PersistSettingsCore(AppSettings settings)
        {
            if (IsTaskHost) return;
            if (_startServices && StartupRegistrationFactory.Supported &&
                (settings.System.StartWithWindows != _persistedStartWithWindows ||
                 settings.System.StartupHidden != _persistedStartupHidden || _startupRegistrationUncertain))
                try
                {
                    SettingsLogger.ZLogDebug($"Startup registration persistence started.");
                    var startupStatus = StartupRegistrationService.Save(settings.System.StartWithWindows,
                        Environment.ProcessPath ?? throw new InvalidOperationException(),
                        () => PersistSettingsWithExplorer(settings), StartupRegistrationFactory.Create());
                    Dispatcher.UIThread.Post(() => _settingsWindow?.RefreshStartupStatus(startupStatus));
                    _startupRegistrationUncertain = false;
                    SettingsLogger.ZLogInformation($"Startup registration persistence completed.");
                }
                catch (Exception error)
                {
                    _startupRegistrationUncertain = true;
                    SettingsLogger.ZLogError(error, $"Startup registration persistence failed.");
                    throw;
                }
            else PersistSettingsWithExplorer(settings);

            _persistedStartWithWindows = settings.System.StartWithWindows;
            _persistedStartupHidden = settings.System.StartupHidden;
        }

        private Task FlushSettingsAsync()
        {
            return _settingsPersistence?.FlushAsync() ?? Task.CompletedTask;
        }
    }
}

using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private bool _persistedStartWithWindows;
        private SettingsPersistenceQueue<AppSettings>? _settingsPersistence;
        private bool _startupRegistrationUncertain;

        private void QueueSettingsSave(AppSettings settings)
        {
            _settingsPersistence ??= new(PersistSettingsAsync, error =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!IsClosing) SetStatus(error.Message);
                });
            });
            _settingsPersistence.RequestSave(settings.Snapshot());
        }

        private Task PersistSettingsAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_startServices && OperatingSystem.IsWindows() &&
                (settings.System.StartWithWindows != _persistedStartWithWindows || _startupRegistrationUncertain))
                try
                {
                    SettingsLogger.ZLogDebug($"Startup registration persistence started.");
                    StartupRegistrationService.Save(settings.System.StartWithWindows,
                        Environment.ProcessPath ?? throw new InvalidOperationException(),
                        () => SettingsWriter(settings), new WindowsStartupRegistration());
                    _startupRegistrationUncertain = false;
                    SettingsLogger.ZLogInformation($"Startup registration persistence completed.");
                }
                catch (Exception error)
                {
                    _startupRegistrationUncertain = true;
                    SettingsLogger.ZLogError(error, $"Startup registration persistence failed.");
                    throw;
                }
            else SettingsWriter(settings);

            _persistedStartWithWindows = settings.System.StartWithWindows;
            return Task.CompletedTask;
        }

        private Task FlushSettingsAsync()
        {
            return _settingsPersistence?.FlushAsync() ?? Task.CompletedTask;
        }
    }
}

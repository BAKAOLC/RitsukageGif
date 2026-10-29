using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private static readonly ILogger SettingsLogger = LogHub.GetLogger("Desktop.Settings");
        private SettingsWindow? _settingsWindow;

        internal async Task OpenSettingsAsync()
        {
            if (IsClosing || IsSelecting) return;
            if (_settingsWindow is { } existing)
            {
                WindowActivation.Show(existing);
                SettingsLogger.ZLogDebug($"Existing settings window activated.");
                return;
            }

            if (IsConfiguring) return;
            SettingsWindow? ownedWindow = null;
            try
            {
                var baseline = Settings.Snapshot();
                var settingsWindow = new SettingsWindow(baseline)
                {
                    ApplySettings = edited =>
                    {
                        UpdateSettings(Settings.MergeChanges(baseline, edited));
                        baseline = edited.Snapshot();
                        SettingsLogger.ZLogDebug($"Settings editor changes submitted.");
                    },
                };
                ownedWindow = settingsWindow;
                settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                _settingsWindow = settingsWindow;
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                settingsWindow.Closed += (_, _) => closed.TrySetResult();
                settingsWindow.Show();
                SettingsLogger.ZLogInformation($"Settings window opened.");
                await closed.Task;

                if (ReferenceEquals(_settingsWindow, ownedWindow)) _settingsWindow = null;
                await FlushSettingsAsync();
                SettingsLogger.ZLogInformation($"Settings window closed and pending changes flushed.");
            }
            catch (Exception error)
            {
                if (error is OperationCanceledException && IsClosing)
                    SettingsLogger.ZLogDebug($"Settings window operation cancelled during shutdown.");
                else SettingsLogger.ZLogError(error, $"Settings window operation failed.");
                SetStatus(error.Message);
            }
            finally
            {
                if (ReferenceEquals(_settingsWindow, ownedWindow)) _settingsWindow = null;
            }
        }
    }
}

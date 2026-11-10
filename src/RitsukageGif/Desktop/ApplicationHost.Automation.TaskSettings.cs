using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private void ConfigureTaskSettingsAutomation(AppSettings initialSnapshot)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!IsTaskHost || IsBusy || IsSelecting || IsRecording || IsClosing)
                throw new InvalidOperationException("Task settings must be initialized on a new isolated task host.");
            Settings = initialSnapshot;
        }

        private void ApplyTaskSettingsAutomation(AppSettings settings, SettingsAutomationOptions? options,
            bool refreshSettingsWindow = true)
        {
            var previous = Settings;
            var passwordChanged = options?.ProxyPassword is not null;
            var networkChanged = settings.Network != previous.Network || passwordChanged;
            if (networkChanged)
            {
                if (settings.Network.Proxy == NetworkProxyKind.None && options?.ProxyPassword is { Length: > 0 })
                    throw new ArgumentException("A proxy password requires an enabled proxy.");
                var preservePassword = !passwordChanged && SameSettingsProxy(settings.Network, previous.Network);
                settings = settings with
                {
                    Network = settings.Network with
                    {
                        CredentialId = preservePassword ? previous.Network.CredentialId : "",
                    },
                };
                Volatile.Write(ref _committedNetwork, settings.Network);
                ApplicationHttp.Configure(settings.Network, preservePassword ? null : options?.ProxyPassword ?? "");
            }

            Settings = settings.Snapshot();
            _globalShortcutsSuspended = !Settings.HotkeyPolicy.ShortcutsEnabled;
            AppTheme.Apply(Settings.Appearance);
            if (previous.Language != Settings.Language) LiveText.ApplyLanguage(Settings.Language);
            SelectionCommands.Configure(Settings.SelectionShortcuts, Settings.MixingKeypadMainKeyboard);
            MouseCommands.Configure(Settings.MouseShortcuts);
            _pinnedImages.Preferences = Settings.PinnedImages;
            _pinnedImages.AnnotationPreferences = Settings.Annotation;
            ConfigureCaptureHistoryBudget();
            View?.RefreshSettingsPresentation(previous, Settings);
            if (refreshSettingsWindow) _settingsWindow?.RefreshAutomationSettings(Settings.Snapshot());
            Notify();
        }
    }
}

using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private ExplorerContextMenuMode? _appliedExplorerMode;
        private ApplicationSyntheticCopyFilter? _copyMacroFilter;
        private DispatcherTimer? _desktopPositionTimer;
        private DesktopToolbarWindow? _desktopToolbar;
        private DesktopToolbarPreferences? _desktopToolbarApplied;
        private Func<string, bool>? _desktopToolbarCanExecute;
        private bool _desktopToolbarSuppressedAtStartup;
        private DispatcherTimer? _desktopToolbarTimer;
        private ExplorerPlatformIntegration? _explorerShell;

        private void PersistSettingsWithExplorer(AppSettings settings)
        {
            if (!_startServices || !OperatingSystem.IsWindows() ||
                _appliedExplorerMode == settings.System.ExplorerContextMenu)
            {
                SettingsWriter(settings);
                return;
            }

            _explorerShell ??= ExplorerPlatformIntegration.CreateWindows();
            _explorerShell.Apply(settings.System.ExplorerContextMenu,
                Environment.ProcessPath ??
                throw new InvalidOperationException("Application executable path is unavailable."),
                UiText.Get("ExplorerPinCaption"), () => SettingsWriter(settings));
            _appliedExplorerMode = settings.System.ExplorerContextMenu;
            SettingsLogger.ZLogInformation($"Explorer integration applied. Mode={settings.System.ExplorerContextMenu}");
        }

        private void InitializeSystemFeatures()
        {
            if (!_startServices) return;
            _desktopToolbarSuppressedAtStartup = Program.StartHidden;
            _services.DisplaysChanged += DesktopToolbarDisplaysChanged;
            _copyMacroFilter = new() { Enabled = Settings.System.IgnoreCopyMacros };
            _desktopToolbarTimer = new() { Interval = TimeSpan.FromMinutes(1) };
            _desktopToolbarTimer.Tick += (_, _) => RefreshDesktopToolbar();
            _desktopToolbarTimer.Start();
            _desktopPositionTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
            _desktopPositionTimer.Tick += (_, _) =>
            {
                _desktopPositionTimer.Stop();
                QueueSettingsSave(Settings);
            };
            RefreshDesktopToolbar();
        }

        private void RefreshDesktopToolbar()
        {
            if (!_startServices || _desktopToolbarTimer is null) return;
            if (_copyMacroFilter is { } filter) filter.Enabled = Settings.System.IgnoreCopyMacros;
            var preferences = Settings.System.DesktopToolbar;
            if (IsClosing || preferences.Mode == DesktopToolbarMode.Disabled)
            {
                _desktopToolbarAdjustment?.Close();
                _desktopToolbar?.Close();
                _desktopToolbar = null;
                _desktopToolbarApplied = null;
                return;
            }

            if (_desktopToolbarSuppressedAtStartup || IsSelecting || IsRecording ||
                preferences.HiddenUntil > DateTimeOffset.Now)
            {
                HideDesktopToolbarAdjustment();
                _desktopToolbar?.Hide();
                return;
            }

            _desktopToolbar ??= new(ExecuteDesktopToolbarAction, () => _ = OpenSettingsAsync(),
                () => _ = AdjustDesktopToolbarAsync(), HideDesktopToolbar, PersistDesktopToolbarPosition,
                async files => { await _pinnedImages.PinFilesAsync(files); },
                text =>
                {
                    if ((Settings.PinnedImages.SupportedContentKinds & PinnedContentKinds.Text) != 0)
                        _pinnedImages.PinDocument(new PinnedTextDocument(text));
                })
            {
                ModeChanged = mode => UpdateSettings(Settings with
                {
                    System = Settings.System with
                    {
                        DesktopToolbar = Settings.System.DesktopToolbar with { Mode = mode, HiddenUntil = null },
                    },
                }),
            };
            var canExecute = _desktopToolbarCanExecute ??= id => CanExecuteAction(DesktopToolbarActions.Binding(id));
            if (_desktopToolbarApplied is not { } applied || !AppSettings.Same(applied, preferences))
            {
                _desktopToolbar.Apply(preferences, canExecute);
                _desktopToolbarApplied = preferences;
            }

            _desktopToolbar.RefreshAvailability(canExecute);
            if (!_desktopToolbar.IsVisible) _desktopToolbar.Show();
            RestoreDesktopToolbarAdjustment();
        }

        private async void ExecuteDesktopToolbarAction(string id)
        {
            try
            {
                var action = DesktopToolbarActions.Binding(id);
                if (!CanExecuteAction(action)) return;
                await ExecuteActionAsync(action);
            }
            catch (Exception error)
            {
                HostLogger.ZLogError(error, $"Desktop toolbar action failed. Action={id}");
                if (!IsClosing) SetStatus(error.Message);
            }
        }

        private void HideDesktopToolbar(bool temporarily)
        {
            var preferences = Settings.System.DesktopToolbar with
            {
                Mode = temporarily ? Settings.System.DesktopToolbar.Mode : DesktopToolbarMode.Disabled,
                HiddenUntil = temporarily ? new DateTimeOffset(DateTime.Today.AddDays(1)) : null,
            };
            UpdateSettings(Settings with { System = Settings.System with { DesktopToolbar = preferences } });
        }

        private void PersistDesktopToolbarPosition(PixelPoint position)
        {
            Settings = Settings with
            {
                System = Settings.System with
                {
                    DesktopToolbar = Settings.System.DesktopToolbar with { Left = position.X, Top = position.Y },
                },
            };
            _desktopPositionTimer?.Stop();
            _desktopPositionTimer?.Start();
        }

        private void StopSystemFeatures()
        {
            _services.DisplaysChanged -= DesktopToolbarDisplaysChanged;
            _desktopToolbarAdjustment?.Close();
            if (_desktopPositionTimer?.IsEnabled == true) QueueSettingsSave(Settings);
            _desktopPositionTimer?.Stop();
            _desktopPositionTimer = null;
            _desktopToolbarTimer?.Stop();
            _desktopToolbarTimer = null;
            _desktopToolbar?.Close();
            _desktopToolbar = null;
            _copyMacroFilter?.Dispose();
            _copyMacroFilter = null;
        }

        private void DesktopToolbarDisplaysChanged(object? sender, EventArgs args)
        {
            _desktopToolbar?.ClampPosition();
        }
    }
}

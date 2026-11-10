using Avalonia.Controls;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private NativeMenu CreateTrayMenu()
        {
            _trayShow = new();
            _trayExit = new();
            _trayActionAnchor = new();
            _trayPresets = new();
            _traySettings = new();
            _trayClearRecords = new();
            _trayClearRecords.Click += (_, _) => _ = RunTrayActionAsync(OpenClearRecordsAsync);
            _trayToggleShortcuts = new() { ToggleType = MenuItemToggleType.CheckBox };
            _trayToggleShortcuts.Click += (_, _) => _ = RunTrayActionAsync(() =>
            {
                ToggleGlobalShortcuts();
                return Task.CompletedTask;
            });
            _trayDisableFullscreen = new() { ToggleType = MenuItemToggleType.CheckBox };
            _trayDisableFullscreen.Click += (_, _) => _ = RunTrayActionAsync(() =>
            {
                Settings.HotkeyPolicy = Settings.HotkeyPolicy with
                {
                    DisableInFullscreen = !Settings.HotkeyPolicy.DisableInFullscreen,
                };
                QueueSettingsSave(Settings);
                RegisterHotkeys();
                RefreshActionMenus();
                Notify();
                return Task.CompletedTask;
            });
            _trayGuide = new();
            _trayGuide.Click += (_, _) => _ = RunTrayActionAsync(() =>
            {
                AppLinks.OpenExternal(AppLinks.DocumentationUrl);
                return Task.CompletedTask;
            });
            _trayUpdate = new() { IsVisible = false };
            _trayUpdate.Click += (_, _) => _ = RunTrayActionAsync(() =>
            {
                if (AvailableUpdate is { } release) UpdateChecker.OpenDownload(release.DownloadUrl);
                return Task.CompletedTask;
            });
            _traySettings.Click += (_, _) => Dispatcher.UIThread.Post(() => _ = OpenTraySettingsAsync());
            _trayShow.Click += (_, _) =>
            {
                if (View is { IsVisible: true }) HideToTray();
                else ShowMainWindow();
            };
            _trayExit.Click += async (_, _) => await ShutdownAsync();
            var menu = new NativeMenu();
            menu.NeedsUpdate += (_, _) =>
            {
                RebuildPinGroupMenus();
                if (_tray is not null) RefreshTrayAppearance();
                RefreshTrayMenu(menu);
            };
            menu.Items.Add(_trayActionAnchor);
            menu.Items.Add(_trayToggleShortcuts);
            menu.Items.Add(_trayDisableFullscreen);
            _traySeparatorSlots.Add((_trayActionAnchor, _trayToggleShortcuts));
            var pinSeparator = new NativeMenuItemSeparator();
            menu.Items.Add(pinSeparator);
            InitializePinMenus(menu);
            _traySeparatorSlots.Add((pinSeparator, _trayPinImages!));
            menu.Items.Add(_trayClearRecords);
            menu.Items.Add(_traySettings);
            var helpSeparator = new NativeMenuItemSeparator();
            menu.Items.Add(helpSeparator);
            menu.Items.Add(_trayGuide);
            menu.Items.Add(_trayUpdate);
            _traySeparatorSlots.Add((helpSeparator, _trayGuide));
            var ownSeparator = new NativeMenuItemSeparator();
            menu.Items.Add(ownSeparator);
            menu.Items.Add(_trayShow);
            menu.Items.Add(_trayRestorePins!);
            menu.Items.Add(_trayUnlockPins!);
            menu.Items.Add(_trayPresets);
            _traySeparatorSlots.Add((ownSeparator, _trayShow));
            var exitSeparator = new NativeMenuItemSeparator();
            menu.Items.Add(exitSeparator);
            menu.Items.Add(_trayExit);
            _traySeparatorSlots.Add((exitSeparator, _trayExit));
            PopulateActionMenus(menu);
            RefreshTrayMenu(menu);
            return menu;
        }
    }
}

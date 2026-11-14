using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private readonly List<(NativeMenuItemSeparator Separator, NativeMenuItem Next)> _traySeparatorSlots = [];
        private TrayIcon? _tray;
        private NativeMenuItem? _trayClearRecords;
        private NativeMenuItem? _trayDisableFullscreen;
        private NativeMenuItem? _trayGuide, _trayUpdate;
        private NativeMenuItem? _trayShow, _trayExit, _traySettings, _trayToggleShortcuts;

        private bool CanHideToTray => !IsClosing && PortableTrayState.HostAvailable &&
                                      _tray is { IsVisible: true, NativeMenuExporter: not null };

        internal bool CanHideMainWindowToTray => CanHideToTray && !IsConfiguring && !IsSelecting &&
                                                 View is { IsVisible: true } view &&
                                                 !view.OwnedWindows.Any(window => window.IsVisible);

        private void InitializeTray()
        {
            if (!_startServices || Application.Current is not { } application) return;
            try
            {
                using var stream = AssetLoader.Open(new("avares://RitsukageGif/!__AvaloniaDefaultWindowIcon"));
                var menu = CreateTrayMenu();
                _tray = new() { Icon = new(stream), Menu = menu, ToolTipText = "RitsukageGif" };
                var icons = TrayIcon.GetIcons(application);
                if (icons is null)
                {
                    icons = [];
                    TrayIcon.SetIcons(application, icons);
                }

                icons.Add(_tray);
                InitializeTrayInput();
                _tray.IsVisible = true;
                Changed += RefreshTray;
                _pinnedImages.Changed += PinnedImagesChanged;
                UiText.LanguageChanged += RefreshTray;
                UiText.LanguageChanged += RebuildPinGroupMenus;
                RefreshTray();
            }
            catch (Exception error)
            {
                DisposeTray();
                Trace.TraceError("Tray initialization failed: {0}", error);
            }
        }

        // ReSharper disable once UnusedMethodReturnValue.Global
        public bool HideToTray()
        {
            if (!CanHideMainWindowToTray || View is not { } view) return false;
            try
            {
                view.SaveCommon();
                view.HideForTray();
            }
            catch (Exception error)
            {
                SetStatus(error.Message);
                return false;
            }

            Notify();
            return true;
        }

        private async Task RunTrayActionAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception error)
            {
                SetStatus(error.Message);
            }
        }

        private async Task OpenTraySettingsAsync()
        {
            if (IsClosing || IsSelecting || IsConfiguring) return;
            try
            {
                await OpenSettingsAsync();
            }
            catch (Exception error)
            {
                SetStatus(error.Message);
            }
        }

        private void RefreshTray()
        {
            if (_tray is null) return;
            RefreshTrayAppearance();
            if (_tray.Menu is { } menu) RefreshTrayMenu(menu);
        }

        private void RefreshTrayMenu(NativeMenu menu)
        {
            var mainVisible = View is { IsVisible: true };
            _trayShow!.Header = UiText.Get(mainVisible ? "TrayHideMain" : "TrayShowMain");
            _trayShow.IsEnabled = mainVisible ? CanHideMainWindowToTray : !IsClosing && !IsSelecting;
            _trayClearRecords!.Header = UiText.Get("ClearRecords");
            _trayClearRecords.IsEnabled = !IsClosing && !IsSelecting &&
                                          _clearRecordsWindow is null or { IsVisible: true };
            _traySettings!.Header = UiText.Get("Settings");
            _traySettings.IsEnabled = !IsClosing && !IsSelecting && !IsConfiguring;
            _trayExit!.Header = UiText.Get("TrayExit");
            _trayExit.IsEnabled = !IsClosing;
            _trayGuide!.Header = UiText.Get("TrayNativeGuide");
            _trayGuide.IsEnabled = !IsClosing && !IsSelecting;
            _trayUpdate!.IsVisible = AvailableUpdate is not null;
            _trayUpdate.IsEnabled = !IsClosing && !IsSelecting && AvailableUpdate is not null;
            _trayUpdate.Header = AvailableUpdate is { } release
                ? UiText.Format("UpdateAvailable", release.Version)
                : UiText.Get("CheckUpdate");
            RefreshActionMenus();
            RefreshPinMenus();
            NormalizeTraySeparators(menu);
        }

        private void NormalizeTraySeparators(NativeMenu menu)
        {
            foreach (var (separator, next) in _traySeparatorSlots)
            {
                if (menu.Items.Contains(separator)) continue;
                var index = menu.Items.IndexOf(next);
                if (index >= 0) menu.Items.Insert(index, separator);
            }

            NativeMenuItemSeparator? pending = null;
            var hasItem = false;
            for (var index = 0; index < menu.Items.Count; index++)
            {
                var item = menu.Items[index];
                if (item is NativeMenuItemSeparator separator)
                {
                    if (!hasItem || pending is not null)
                    {
                        menu.Items.RemoveAt(index--);
                        continue;
                    }

                    separator.IsVisible = true;
                    pending = separator;
                    continue;
                }

                if (item is not NativeMenuItem { IsVisible: true }) continue;
                pending = null;
                hasItem = true;
            }

            if (pending is not null) menu.Items.Remove(pending);
        }

        private static KeyGesture? ParseTrayGesture(string shortcut)
        {
            if (string.IsNullOrWhiteSpace(shortcut)) return null;
            try
            {
                return KeyGestureDisplay.Parse(shortcut);
            }
            catch (Exception error) when (error is ArgumentException or FormatException)
            {
                return null;
            }
        }

        private void DisposeTray()
        {
            DisposeTrayInput();
            DisposeTrayGroupIcons();
            _trayDisableFullscreen = null;
            _trayGuide = _trayUpdate = null;
            _trayAppearance = null;
            Changed -= RefreshTray;
            _pinnedImages.Changed -= PinnedImagesChanged;
            UiText.LanguageChanged -= RefreshTray;
            UiText.LanguageChanged -= RebuildPinGroupMenus;
            if (_tray is not { } tray) return;
            if (!IsClosing)
            {
                _pinnedImages.RestoreInteraction();
                if (View is { IsHiddenInTray: true } view)
                {
                    view.RestoreFromTray();
                    WindowActivation.Show(view);
                }
            }

            _tray = null;
            _trayActionAnchor = null;
            _trayPresets = null;
            _trayToggleShortcuts = null;
            _trayClearRecords = null;
            _traySeparatorSlots.Clear();
            _actionMenus.Clear();
            _trayPinGroups = _trayPinImages = _trayRestorePins = _trayUnlockPins = null;
            if (Application.Current is { } trayApplication) TrayIcon.GetIcons(trayApplication)?.Remove(tray);
            tray.Dispose();
        }

        private void PinnedImagesChanged(object? sender, EventArgs args)
        {
            RebuildPinGroupMenus();
            RefreshTray();
        }
    }
}

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private readonly List<(NativeMenuItemSeparator Separator, NativeMenuItem Next)> _traySeparatorSlots = [];
        private TrayIcon? _tray;
        private NativeMenuItem? _trayShow, _trayExit, _traySettings;
        private bool CanHideToTray => !IsClosing && _tray is { IsVisible: true, NativeMenuExporter: not null };

        internal bool CanHideMainWindowToTray => CanHideToTray && !IsConfiguring && !IsSelecting &&
                                                 View is { IsVisible: true } view &&
                                                 !view.OwnedWindows.Any(window => window.IsVisible);

        private void InitializeTray()
        {
            if (!_startServices || Application.Current is not { } application) return;
            try
            {
                using var stream = AssetLoader.Open(new("avares://RitsukageGif/!__AvaloniaDefaultWindowIcon"));
                _trayShow = new();
                _trayExit = new();
                _trayActionAnchor = new();
                _trayPresets = new();
                _traySettings = new();
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
                    RefreshTray();
                };
                menu.Items.Add(_trayActionAnchor);
                InitializePinMenus(menu);
                _traySeparatorSlots.Add((_trayActionAnchor, _trayPinGroups!));
                var presetSeparator = new NativeMenuItemSeparator();
                menu.Items.Add(presetSeparator);
                menu.Items.Add(_trayPresets);
                _traySeparatorSlots.Add((presetSeparator, _trayPresets));
                var systemSeparator = new NativeMenuItemSeparator();
                menu.Items.Add(systemSeparator);
                menu.Items.Add(_trayShow);
                _traySeparatorSlots.Add((systemSeparator, _trayShow));
                menu.Items.Add(_traySettings);
                menu.Items.Add(_trayExit);
                _tray = new() { Icon = new(stream), Menu = menu, ToolTipText = "RitsukageGif" };
                InitializeTrayInput();
                var icons = TrayIcon.GetIcons(application);
                if (icons is null)
                {
                    icons = [];
                    TrayIcon.SetIcons(application, icons);
                }

                icons.Add(_tray);
                _tray.IsVisible = true;
                Changed += RefreshTray;
                _pinnedImages.Changed += PinnedImagesChanged;
                UiText.LanguageChanged += RefreshTray;
                UiText.LanguageChanged += RebuildPinGroupMenus;
                RebuildActionMenus();
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
            var mainVisible = View is { IsVisible: true };
            _trayShow!.Header = UiText.Get(mainVisible ? "TrayHideMain" : "TrayShowMain");
            _trayShow.IsEnabled = mainVisible ? CanHideMainWindowToTray : !IsClosing && !IsSelecting;
            _traySettings!.Header = UiText.Get("Settings");
            _traySettings.IsEnabled = !IsClosing && !IsSelecting && !IsConfiguring;
            _trayExit!.Header = UiText.Get("TrayExit");
            _trayExit.IsEnabled = !IsClosing;
            RefreshActionMenus();
            RefreshPinMenus();
            if (_tray.Menu is { } menu) NormalizeTraySeparators(menu);
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

        private async Task PinClipboardOrFileAsync()
        {
            if (IsClosing || IsSelecting) return;
            var context = _services.Context;
            if (context.Clipboard is not { } clipboard || !await _pinnedImages.PinClipboardAsync(clipboard))
                await _pinnedImages.OpenImageAsync(context);
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

using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private static readonly ILogger PinLogger = LogHub.GetLogger("Desktop.Pins.Host");
        private readonly Task _pinStartup = Task.CompletedTask;
        private readonly CancellationTokenSource _pinStartupCancellation = new();
        private bool _pinSessionReady;
        private NativeMenuItem? _trayPinGroups;
        private NativeMenuItem? _trayPinImages;
        private NativeMenuItem? _trayRestorePins;
        private NativeMenuItem? _trayUnlockPins;
        private readonly List<Bitmap> _trayGroupIcons = [];

        private void InitializePinMenus(NativeMenu menu)
        {
            _trayPinGroups = new();
            _trayPinImages = new();
            _trayRestorePins = new();
            _trayUnlockPins = new();
            _trayRestorePins.Click += async (_, _) => await RestorePinnedSessionAsync();
            _trayUnlockPins.Click += (_, _) => _pinnedImages.RestoreInteraction();
            menu.Items.Add(_trayPinImages);
            menu.Items.Add(_trayPinGroups);
            RebuildPinGroupMenus();
        }

        private void RebuildPinGroupMenus()
        {
            if (_trayPinGroups is null) return;
            var menu = new NativeMenu();
            var create = new NativeMenuItem(UiText.Get("TrayNativeCreateGroup"));
            create.Click += async (_, _) =>
            {
                try
                {
                    await _pinnedImages.CreateGroupInteractiveAsync();
                }
                catch (Exception error)
                {
                    SetStatus(error.Message);
                }
            };
            var manage = new NativeMenuItem(UiText.Get("TrayNativeManageGroups"));
            manage.Click += (_, _) => _pinnedImages.OpenGroupManager();
            menu.Items.Add(create);
            menu.Items.Add(manage);
            menu.Items.Add(new NativeMenuItemSeparator());
            var icons = new List<Bitmap>();
            foreach (var group in _pinnedImages.Groups)
            {
                var count = _pinnedImages.GetGroupPinCounts(group.Id);
                var name = group.Name.Length == 0 ? UiText.Get("PinnedImageUngrouped") : group.Name;
                var active = group.Id == _pinnedImages.Preferences.ActiveGroup;
                var item = new NativeMenuItem($"{name}\t  {count.Visible,2}/{count.Total,-2}")
                {
                    ToggleType = group.Color is null && active ? MenuItemToggleType.CheckBox : MenuItemToggleType.None,
                    IsChecked = group.Color is null && active,
                };
                if (group.Color is not null)
                {
                    var icon = PinnedNativeGroupIcon.Create(group, active);
                    icons.Add(icon);
                    item.Icon = icon;
                }

                item.Click += (_, _) => _pinnedImages.SwitchToGroup(group.Id);
                menu.Items.Add(item);
            }

            _trayPinGroups.Menu = menu;
            DisposeTrayGroupIcons();
            _trayGroupIcons.AddRange(icons);
            _trayPinImages!.Menu = CreatePinOperationsMenu();
            RefreshPinMenus();
        }

        private void RefreshPinMenus()
        {
            if (_trayPinGroups is null || _trayRestorePins is null || _trayUnlockPins is null) return;
            _trayPinGroups.Header = UiText.Get("TrayNativePinGroups");
            _trayPinGroups.IsVisible = _trayPinGroups.Menu?.Items.Count > 0;
            _trayPinGroups.IsEnabled = !IsClosing && !IsSelecting;
            if (_trayPinImages is not null)
            {
                _trayPinImages.Header = UiText.Format("TrayNativePinCount", _pinnedImages.OpenWindowCount);
                _trayPinImages.IsVisible = true;
                _trayPinImages.IsEnabled = !IsClosing && !IsSelecting && _pinnedImages.OpenWindowCount != 0;
            }

            _trayRestorePins.Header = UiText.Get("PinnedImageRestoreSession");
            _trayRestorePins.IsVisible = _pinSessionReady && _pinnedImages.HasRestorableSession;
            _trayRestorePins.IsEnabled = !IsClosing && !IsSelecting && _pinSessionReady;
            _trayUnlockPins.Header = UiText.Get("PinnedImageRestoreInteraction");
            _trayUnlockPins.IsVisible = _pinnedImages.HasImages;
            _trayUnlockPins.IsEnabled = !IsClosing && !IsSelecting && _pinnedImages.HasImages;
        }

        private async Task RestorePinnedSessionAsync()
        {
            if (IsClosing || IsSelecting) return;
            try
            {
                PinLogger.ZLogDebug($"Pinned image session restore requested.");
                await _pinStartup;
                var rejected = await _pinnedImages.RestoreSessionAsync(_pinStartupCancellation.Token);
                if (!IsClosing && rejected > 0) SetStatus(UiText.Format("PinnedImagesSkipped", rejected));
                if (!IsClosing && !IsSelecting) _pinnedImages.ShowAll();
                PinLogger.ZLogInformation($"Pinned image session restore completed. RejectedCount={rejected}");
            }
            catch (OperationCanceledException) when (IsClosing)
            {
                PinLogger.ZLogDebug($"Pinned image session restore cancelled during shutdown.");
            }
            catch (Exception error)
            {
                PinLogger.ZLogError(error, $"Pinned image session restore failed.");
                if (!IsClosing) SetStatus(error.Message);
            }
        }

        private async Task InitializePinnedSessionAsync()
        {
            try
            {
                PinLogger.ZLogDebug($"Pinned image session initialization started.");
                var directory = Path.Combine(ApplicationStorage.DataDirectory, "pins");
                var rejected = await _pinnedImages.ConfigureSessionAsync(directory, Settings.RestorePinnedImages,
                    _pinStartupCancellation.Token);
                if (!IsClosing && rejected > 0) SetStatus(UiText.Format("PinnedImagesSkipped", rejected));
                _pinSessionReady = true;
                RefreshTray();
                PinLogger.ZLogInformation($"Pinned image session initialization completed. RejectedCount={rejected}");
            }
            catch (OperationCanceledException) when (_pinStartupCancellation.IsCancellationRequested)
            {
                PinLogger.ZLogDebug($"Pinned image session initialization cancelled.");
            }
            catch (Exception error)
            {
                PinLogger.ZLogError(error, $"Pinned image session initialization failed.");
                if (!IsClosing) SetStatus(error.Message);
            }
        }

        private async Task StopPinnedSessionAsync()
        {
            var token = _pinStartupCancellation.Token;
            try
            {
                await RunShutdownStageAsync("pinned image cancellation", _pinStartupCancellation.CancelAsync,
                    CancellationToken.None);
                await RunShutdownStageAsync("pinned image initialization", () => _pinStartup, token);
                await RunShutdownStageAsync("pinned history operations", _pinnedImages.StopHistoryAsync,
                    CancellationToken.None);
                if (_startServices)
                    await RunShutdownStageAsync("pinned image persistence",
                        () => _pinnedImages.FlushSessionAsync(CancellationToken.None), CancellationToken.None);
            }
            finally
            {
                try
                {
                    RunShutdownStage("pinned image manager", _pinnedImages.Dispose);
                }
                finally
                {
                    _pinStartupCancellation.Dispose();
                }
            }
        }
    }
}

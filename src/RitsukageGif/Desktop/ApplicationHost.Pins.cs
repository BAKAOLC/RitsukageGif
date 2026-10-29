using Avalonia.Controls;

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

        private void InitializePinMenus(NativeMenu menu)
        {
            _trayPinGroups = new();
            _trayPinImages = new();
            _trayRestorePins = new();
            _trayUnlockPins = new();
            _trayRestorePins.Click += async (_, _) => await RestorePinnedSessionAsync();
            _trayUnlockPins.Click += (_, _) => _pinnedImages.RestoreInteraction();
            menu.Items.Add(_trayPinGroups);
            menu.Items.Add(_trayPinImages);
            menu.Items.Add(_trayRestorePins);
            menu.Items.Add(_trayUnlockPins);
            RebuildPinGroupMenus();
        }

        private void RebuildPinGroupMenus()
        {
            if (_trayPinGroups is null) return;
            var menu = new NativeMenu();
            foreach (var group in _pinnedImages.Groups)
            {
                var commands = new NativeMenu();
                var show = new NativeMenuItem(UiText.Get("PinnedImageShowGroup"));
                var hide = new NativeMenuItem(UiText.Get("PinnedImageHideGroup"));
                var close = new NativeMenuItem(UiText.Get("PinnedImageCloseGroup"));
                show.Click += (_, _) => _pinnedImages.ShowGroup(group);
                hide.Click += (_, _) => _pinnedImages.HideGroup(group);
                close.Click += (_, _) => _pinnedImages.CloseGroup(group);
                commands.Items.Add(show);
                commands.Items.Add(hide);
                commands.Items.Add(close);
                menu.Items.Add(new NativeMenuItem(group) { Menu = commands });
            }

            _trayPinGroups.Menu = menu;
            var images = new NativeMenu();
            foreach (var window in _pinnedImages.Images)
            {
                var item = new NativeMenuItem(window.Title ?? UiText.Get("ScreenshotPin"))
                {
                    ToggleType = NativeMenuItemToggleType.CheckBox,
                    IsChecked = window.IsVisible,
                };
                item.Click += (_, _) => _pinnedImages.ShowImage(window);
                images.Items.Add(item);
            }

            _trayPinImages?.Menu = images;
            RefreshPinMenus();
        }

        private void RefreshPinMenus()
        {
            if (_trayPinGroups is null || _trayRestorePins is null || _trayUnlockPins is null) return;
            _trayPinGroups.Header = UiText.Get("PinnedImageGroup");
            _trayPinGroups.IsVisible = _trayPinGroups.Menu?.Items.Count > 0;
            _trayPinGroups.IsEnabled = !IsClosing && !IsSelecting;
            if (_trayPinImages is not null)
            {
                _trayPinImages.Header = UiText.Format("TrayPinnedImagesCount", _pinnedImages.Images.Count);
                _trayPinImages.IsVisible = _pinnedImages.HasImages;
                _trayPinImages.IsEnabled = !IsClosing && !IsSelecting;
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

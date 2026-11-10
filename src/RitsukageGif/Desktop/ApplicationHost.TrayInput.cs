using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private TrayClickSequencer? _trayClicks;
        private WindowsTrayInput? _trayInput;

        private void InitializeTrayInput()
        {
            _trayClicks = new(ResolveTrayGesture,
                action => Dispatcher.UIThread.Post(() => _ = RunTrayActionAsync(() => ExecuteTrayActionAsync(action))),
                (action, delay) => DispatcherTimer.RunOnce(action, delay));
            if (OperatingSystem.IsWindows())
            {
                _trayInput = WindowsTrayInput.Attach(message => _trayClicks.Process(message));
                PortableTrayState.WindowsExtendedInput = _trayInput is not null;
                PortableTrayState.Notify();
            }

            PortableTrayState.Gesture += PortableTrayGesture;
            PortableTrayState.Changed += PortableTrayAvailabilityChanged;
            if (_trayInput is null && PortableTrayState.Current is null && _tray is { } tray)
                tray.Clicked += (_, _) => _trayClicks.ProcessGesture(TrayMouseGesture.SingleClick, false);
        }

        private void PortableTrayGesture(TrayMouseGesture gesture)
        {
            _trayClicks?.ProcessGesture(gesture, PortableTrayState.Supports(TrayMouseGesture.DoubleClick));
        }

        private void PortableTrayAvailabilityChanged()
        {
            if (IsClosing) return;
            RefreshTray();
            if (!PortableTrayState.HostAvailable && View is { IsHiddenInTray: true } &&
                !_desktop.Windows.Any(window => window.IsVisible)) ShowMainWindow();
        }

        private SystemTrayAction ResolveTrayGesture(TrayMouseGesture gesture)
        {
            return gesture switch
            {
                TrayMouseGesture.SingleClick => Settings.System.TraySingleClick,
                TrayMouseGesture.DoubleClick => Settings.System.TrayDoubleClick,
                _ => Settings.System.TrayMiddleClick,
            };
        }

        private Task ExecuteTrayActionAsync(SystemTrayAction action)
        {
            if (IsClosing || IsSelecting) return Task.CompletedTask;
            switch (action)
            {
                case SystemTrayAction.None: return Task.CompletedTask;
                case SystemTrayAction.Screenshot: return ScreenshotAsync();
                case SystemTrayAction.ScreenshotAndPin:
                    return ScreenshotAsync(Settings.CaptureSource, confirmAction: ScreenshotOutputAction.Pin,
                        autoConfirm: true);
                case SystemTrayAction.ToggleGlobalShortcuts:
                    if (!IsConfiguring) ToggleGlobalShortcuts();
                    break;
                case SystemTrayAction.TogglePinnedImages: return ToggleAllPinnedVisibilityAsync();
                case SystemTrayAction.OpenSettings: return OpenTraySettingsAsync();
                default: return Task.CompletedTask;
            }

            return Task.CompletedTask;
        }

        private void DisposeTrayInput()
        {
            PortableTrayState.Gesture -= PortableTrayGesture;
            PortableTrayState.Changed -= PortableTrayAvailabilityChanged;
            _trayInput?.Dispose();
            _trayInput = null;
            if (OperatingSystem.IsWindows())
            {
                PortableTrayState.WindowsExtendedInput = false;
                PortableTrayState.Notify();
            }

            _trayClicks?.Dispose();
            _trayClicks = null;
        }
    }
}

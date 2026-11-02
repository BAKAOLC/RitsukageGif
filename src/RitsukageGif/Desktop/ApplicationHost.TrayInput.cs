using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private TrayClickSequencer? _trayClicks;
        private WindowsTrayInput? _trayInput;

        private void InitializeTrayInput()
        {
            _trayClicks = new(gesture => Dispatcher.UIThread.Post(() =>
                    _ = RunTrayActionAsync(() => ExecuteTrayGestureAsync(gesture))),
                (action, delay) => DispatcherTimer.RunOnce(action, delay), () => OperatingSystem.IsWindows()
                    ? WindowsTrayInput.DoubleClickTime
                    : Application.Current?.PlatformSettings?.GetDoubleTapTime(PointerType.Mouse) ??
                      TimeSpan.FromMilliseconds(500));
            if (OperatingSystem.IsWindows())
            {
                _trayInput = WindowsTrayInput.Attach(message => _trayClicks.Process(message,
                    Settings.System.TrayDoubleClick != SystemTrayAction.None));
                PortableTrayState.WindowsExtendedInput = _trayInput is not null;
                PortableTrayState.Notify();
            }

            PortableTrayState.Gesture += PortableTrayGesture;
            PortableTrayState.Changed += PortableTrayAvailabilityChanged;
            if (_trayInput is null && PortableTrayState.Current is null && _tray is { } tray)
                tray.Clicked += (_, _) => Dispatcher.UIThread.Post(() =>
                    _ = RunTrayActionAsync(() => ExecuteTrayGestureAsync(TrayMouseGesture.SingleClick)));
        }

        private void PortableTrayGesture(TrayMouseGesture gesture)
        {
            _trayClicks?.ProcessGesture(gesture, PortableTrayState.Supports(TrayMouseGesture.DoubleClick) &&
                                                 Settings.System.TrayDoubleClick != SystemTrayAction.None);
        }

        private void PortableTrayAvailabilityChanged()
        {
            if (IsClosing) return;
            RefreshTray();
            if (!PortableTrayState.HostAvailable && View is { IsHiddenInTray: true } &&
                !_desktop.Windows.Any(window => window.IsVisible)) ShowMainWindow();
        }

        private Task ExecuteTrayGestureAsync(TrayMouseGesture gesture)
        {
            if (IsClosing || IsSelecting) return Task.CompletedTask;
            var action = gesture switch
            {
                TrayMouseGesture.SingleClick => Settings.System.TraySingleClick,
                TrayMouseGesture.DoubleClick => Settings.System.TrayDoubleClick,
                _ => Settings.System.TrayMiddleClick,
            };
            switch (action)
            {
                case SystemTrayAction.None: return Task.CompletedTask;
                case SystemTrayAction.ShowMainWindow: ShowMainWindow(); break;
                case SystemTrayAction.Screenshot: return ScreenshotAsync();
                case SystemTrayAction.PinClipboard: return PinClipboardOrFileAsync();
                case SystemTrayAction.ToggleRecording: return ToggleRecordingAsync();
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

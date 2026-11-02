using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal static class PortableTrayState
    {
        internal static PortableTrayIcon? Current { get; private set; }
        internal static bool WindowsExtendedInput { get; set; }

        internal static bool HostAvailable => Current?.HostAvailable ?? !OperatingSystem.IsLinux();
        internal static event Action? Changed;
        internal static event Action<TrayMouseGesture>? Gesture;

        internal static bool Supports(TrayMouseGesture gesture)
        {
            return (OperatingSystem.IsWindows() && (gesture == TrayMouseGesture.SingleClick || WindowsExtendedInput)) ||
                   (Current is { IsReady: true, HostAvailable: true } icon &&
                    (gesture != TrayMouseGesture.DoubleClick || icon.SupportsDoubleClick));
        }

        internal static void Attach(PortableTrayIcon icon)
        {
            Current = icon;
            Notify();
        }

        internal static void Detach(PortableTrayIcon icon)
        {
            if (!ReferenceEquals(Current, icon)) return;
            Current = null;
            Notify();
        }

        internal static void Notify()
        {
            if (Dispatcher.UIThread.CheckAccess()) Changed?.Invoke();
            else Dispatcher.UIThread.Post(() => Changed?.Invoke());
        }

        internal static void Click(TrayMouseGesture gesture)
        {
            if (Dispatcher.UIThread.CheckAccess()) Gesture?.Invoke(gesture);
            else Dispatcher.UIThread.Post(() => Gesture?.Invoke(gesture));
        }
    }
}

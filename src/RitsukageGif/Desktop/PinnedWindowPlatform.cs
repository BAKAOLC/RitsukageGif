using Avalonia.Controls;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    internal static partial class PinnedWindowPlatform
    {
        public static bool CanSetMouseThrough(Window window)
        {
            var handle = window.TryGetPlatformHandle();
            if (handle is null || handle.Handle == 0) return false;
            if (OperatingSystem.IsWindows()) return handle.HandleDescriptor == "HWND";
            if (OperatingSystem.IsMacOS()) return GetMacWindow(window) != 0;
            return OperatingSystem.IsLinux() && handle.HandleDescriptor == "XID" &&
                   window.PlatformImpl is IPopupImpl && CanSetLinuxMouseThrough();
        }

        public static void SetMouseThrough(Window window, bool enabled, ref nint originalStyle)
        {
            if (OperatingSystem.IsWindows())
                SetWindowsMouseThrough(window, enabled, ref originalStyle);
            else if (OperatingSystem.IsMacOS())
                SetMacMouseThrough(window, enabled, ref originalStyle);
            else if (OperatingSystem.IsLinux() && window.TryGetPlatformHandle()?.HandleDescriptor == "XID" &&
                     window.PlatformImpl is IPopupImpl input)
                input.SetHitTestVisible(!enabled);
            else
                throw new PlatformNotSupportedException();
        }
    }
}

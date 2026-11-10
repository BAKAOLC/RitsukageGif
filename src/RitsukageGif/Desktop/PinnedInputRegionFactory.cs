using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal static class PinnedInputRegionFactory
    {
        internal static bool CanCreate(Window window)
        {
            if (window.TryGetPlatformHandle() is not { Handle: not 0 } native) return false;
            if (OperatingSystem.IsWindows()) return native.HandleDescriptor == "HWND";
            return OperatingSystem.IsLinux() && native.HandleDescriptor == "XID" &&
                   X11WindowSystem.CanSetInputRegion(native.Handle);
        }

        internal static IPinnedInputRegionBackend? TryCreate(Window window, Func<bool> mouseThrough)
        {
            if (!CanCreate(window)) return null;
            var native = window.TryGetPlatformHandle()!;
            return OperatingSystem.IsWindows()
                ? new PinnedWindowsInputRegion(native.Handle)
                : new PinnedX11InputRegion(native.Handle, mouseThrough);
        }
    }
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RitsukageGif.Desktop
{
    [SupportedOSPlatform("windows")]
    internal static partial class WindowsExternalWindowTopmost
    {
        internal static bool Toggle(Avalonia.PixelPoint point)
        {
            var window = WindowFromPoint(new() { X = point.X, Y = point.Y });
            if (window == 0) throw new InvalidOperationException(UiText.Get("GlobalActionWindowAtPointerUnavailable"));
            var root = GetAncestor(window, 2);
            if (root != 0) window = root;
            var enabled = !IsTopmost(window);
            if (SetWindowPos(window, enabled ? -1 : -2, 0, 0, 0, 0, 3) == 0)
                throw new IOException("Unable to change the window topmost state.",
                    new Win32Exception(Marshal.GetLastPInvokeError()));
            if (IsTopmost(window) != enabled)
                throw new IOException("The window topmost state did not change.");
            return enabled;
        }

        private static bool IsTopmost(nint window)
        {
            var style = GetWindowLongW(window, -20);
            var error = Marshal.GetLastPInvokeError();
            if (style == 0 && error != 0)
                throw new IOException("Unable to read the window topmost state.", new Win32Exception(error));
            return (style & 8) != 0;
        }

        [LibraryImport("user32.dll")]
        private static partial nint WindowFromPoint(NativeMethods.Point point);

        [LibraryImport("user32.dll")]
        private static partial nint GetAncestor(nint window, uint flags);

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial uint GetWindowLongW(nint window, int index);

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial int SetWindowPos(nint window, nint after, int x, int y, int width, int height,
            uint flags);
    }
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal static partial class PinnedWindowPlatform
    {
        [SupportedOSPlatform("windows")]
        private static void SetWindowsMouseThrough(Window window, bool enabled, ref nint originalStyle)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var handle = window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException();
            const int extendedStyle = -20;
            const nint transparent = 0x20;
            const nint layered = 0x80000;
            var current = GetWindowLongPtr(handle, extendedStyle);
            if (enabled) originalStyle = current;
            var updated = enabled ? current | transparent | layered : current & ~transparent;
            if (!enabled && (originalStyle & layered) == 0 && window.Opacity >= 1) updated &= ~layered;
            Marshal.SetLastPInvokeError(0);
            if (SetWindowLongPtr(handle, extendedStyle, updated) == 0 && Marshal.GetLastPInvokeError() != 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (enabled && !SetLayeredWindowAttributes(handle, 0, (byte)Math.Round(window.Opacity * 255), 2))
            {
                var error = Marshal.GetLastPInvokeError();
                SetWindowLongPtr(handle, extendedStyle, originalStyle);
                throw new Win32Exception(error);
            }
        }

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static partial nint GetWindowLongPtr(nint window, int index);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static partial nint SetWindowLongPtr(nint window, int index, nint value);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetLayeredWindowAttributes(nint window, uint color, byte alpha, uint flags);
    }
}

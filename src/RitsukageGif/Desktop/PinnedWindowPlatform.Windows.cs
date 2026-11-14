using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal static partial class PinnedWindowPlatform
    {
        private const uint LayeredStyle = 0x80000;
        private const uint TransparentStyle = 0x20;

        [SupportedOSPlatform("windows")]
        private static void SetWindowsMouseThrough(Window window, bool enabled, ref nint originalStyle)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var handle = window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException();
            const int extendedStyle = -20;
            var isPin = window is IPinnedWindow;
            const nint layered = (nint)LayeredStyle;
            const nint transparent = (nint)TransparentStyle;
            var current = GetWindowLongPtr(handle, extendedStyle);
            if (enabled) originalStyle = current;
            var updated = enabled ? current | transparent | layered : current & ~transparent;
            if (!enabled && (originalStyle & layered) == 0 && (isPin || window.Opacity >= 1)) updated &= ~layered;
            Marshal.SetLastPInvokeError(0);
            if (SetWindowLongPtr(handle, extendedStyle, updated) == 0 && Marshal.GetLastPInvokeError() != 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (enabled && (!isPin || (originalStyle & layered) == 0) &&
                !SetLayeredWindowAttributes(handle, 0, isPin ? byte.MaxValue : (byte)Math.Round(window.Opacity * 255),
                    2))
            {
                var error = Marshal.GetLastPInvokeError();
                SetWindowLongPtr(handle, extendedStyle, originalStyle);
                throw new Win32Exception(error);
            }

            if (!isPin) return;
            if (enabled) Win32Properties.AddWindowStylesCallback(window, KeepWindowsMouseThroughStyles);
            else Win32Properties.RemoveWindowStylesCallback(window, KeepWindowsMouseThroughStyles);
        }

        private static (uint Style, uint ExtendedStyle) KeepWindowsMouseThroughStyles(uint style, uint extendedStyle)
        {
            return (style, extendedStyle | LayeredStyle | TransparentStyle);
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

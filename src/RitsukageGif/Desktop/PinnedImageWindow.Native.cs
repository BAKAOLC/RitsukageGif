using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private nint _inputStyle;

        private void ApplyMouseThrough(bool enabled)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var handle = TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException();
            const int extendedStyle = -20;
            const nint transparent = 0x20;
            const nint layered = 0x80000;
            if (enabled)
            {
                _inputStyle = GetWindowLongPtr(handle, extendedStyle);
                Marshal.SetLastPInvokeError(0);
                if (SetWindowLongPtr(handle, extendedStyle, _inputStyle | transparent | layered) == 0 &&
                    Marshal.GetLastPInvokeError() != 0)
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                if (!SetLayeredWindowAttributes(handle, 0, (byte)Math.Round(Opacity * 255), 2))
                {
                    var error = Marshal.GetLastPInvokeError();
                    SetWindowLongPtr(handle, extendedStyle, _inputStyle);
                    throw new Win32Exception(error);
                }
            }
            else
            {
                var current = GetWindowLongPtr(handle, extendedStyle);
                var restored = current & ~transparent;
                if ((_inputStyle & layered) == 0 && Opacity >= 1) restored &= ~layered;
                Marshal.SetLastPInvokeError(0);
                if (SetWindowLongPtr(handle, extendedStyle, restored) == 0 && Marshal.GetLastPInvokeError() != 0)
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
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

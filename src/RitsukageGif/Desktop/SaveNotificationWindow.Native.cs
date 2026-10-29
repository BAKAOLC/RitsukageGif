using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SaveNotificationWindow
    {
        private void ConfigureNativeWindow()
        {
            if (!OperatingSystem.IsWindows() || TryGetPlatformHandle() is not { } handle) return;
            var style = GetWindowLongPtr(handle.Handle, -20);
            if (SetWindowLongPtr(handle.Handle, -20, style | 0x08000080) == 0 && Marshal.GetLastPInvokeError() != 0)
                Trace.TraceWarning("Unable to configure notification window styles: {0}",
                    Marshal.GetLastPInvokeError());
            if (!SetWindowDisplayAffinity(handle.Handle, 0x11))
                Trace.TraceWarning("Unable to exclude notification window from capture: {0}",
                    Marshal.GetLastPInvokeError());
        }

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static partial nint GetWindowLongPtr(nint window, int index);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static partial nint SetWindowLongPtr(nint window, int index, nint value);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWindowDisplayAffinity(nint window, uint affinity);
    }
}

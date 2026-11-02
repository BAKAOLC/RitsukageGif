using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class DesktopToolbarWindow
    {
        private void ConfigureNativeWindow()
        {
            if (!OperatingSystem.IsWindows() || TryGetPlatformHandle() is not { } handle) return;
            var style = GetWindowLongPtr(handle.Handle, -20);
            var previous = SetWindowLongPtr(handle.Handle, -20, (style | 0x08000080) & ~0x40000);
            var error = Marshal.GetLastPInvokeError();
            if (previous == 0 && error != 0)
                LogHub.GetLogger("Desktop.Toolbar")
                    .ZLogWarning($"Unable to configure desktop toolbar window styles. Error={error}");
            if (!SetWindowDisplayAffinity(handle.Handle, 0x11))
                LogHub.GetLogger("Desktop.Toolbar")
                    .ZLogWarning(
                        $"Unable to exclude desktop toolbar from capture. Error={Marshal.GetLastPInvokeError()}");
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

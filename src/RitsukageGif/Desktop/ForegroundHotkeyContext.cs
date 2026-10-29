using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RitsukageGif.Desktop
{
    internal sealed partial record ForegroundHotkeyContext(string? Program, bool Fullscreen)
    {
        internal static unsafe ForegroundHotkeyContext Read(bool fullscreen, bool program)
        {
            var window = GetForegroundWindow();
            if (window == 0) return new(null, false);
            string? path = null;
            if (program)
                if (GetWindowThreadProcessId(window, out var processId) != 0)
                {
                    using var process = OpenProcess(0x1000, 0, processId);
                    if (!process.IsInvalid)
                    {
                        Span<char> buffer = stackalloc char[32768];
                        var length = (uint)buffer.Length;
                        fixed (char* data = buffer)
                            if (QueryFullProcessImageName(process, 0, data, ref length) != 0)
                                path = new(data, 0, (int)length);
                    }
                }

            var isFullscreen = false;
            if (fullscreen && window != GetShellWindow() && window != GetDesktopWindow())
            {
                isFullscreen = ShQueryUserNotificationState(out var state) >= 0 && state == 3;
                if (!isFullscreen && IsZoomed(window) == 0 && (GetWindowLong(window, -16) & 0x00C40000) == 0 &&
                    NativeMethods.GetWindowRect(window, out var bounds) != 0)
                {
                    var monitor = MonitorFromWindow(window, 2);
                    var info = new MonitorInfo { Size = (uint)sizeof(MonitorInfo) };
                    if (monitor != 0 && GetMonitorInfo(monitor, ref info) != 0)
                        isFullscreen = bounds.Left == info.Monitor.Left && bounds.Top == info.Monitor.Top &&
                                       bounds.Right == info.Monitor.Right && bounds.Bottom == info.Monitor.Bottom;
                }
            }

            return new(path, isFullscreen);
        }

        [LibraryImport("user32.dll")]
        private static partial nint GetForegroundWindow();

        [LibraryImport("user32.dll")]
        private static partial nint GetShellWindow();

        [LibraryImport("user32.dll")]
        private static partial nint GetDesktopWindow();

        [LibraryImport("user32.dll")]
        private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

        [LibraryImport("kernel32.dll")]
        private static partial SafeProcessHandle OpenProcess(uint access, int inherit, uint processId);

        [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW")]
        private static unsafe partial int QueryFullProcessImageName(SafeProcessHandle process, uint flags, char* name,
            ref uint length);

        [LibraryImport("shell32.dll", EntryPoint = "SHQueryUserNotificationState")]
        private static partial int ShQueryUserNotificationState(out int state);

        [LibraryImport("user32.dll")]
        private static partial int IsZoomed(nint window);

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static partial int GetWindowLong(nint window, int index);

        [LibraryImport("user32.dll")]
        private static partial nint MonitorFromWindow(nint window, uint flags);

        [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
        private static partial int GetMonitorInfo(nint monitor, ref MonitorInfo info);
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class WindowLocator
    {
        public static WindowTarget? Refresh(WindowTarget target)
        {
            if (OperatingSystem.IsLinux() && !DesktopCapture.UsesSourceSelection)
                return X11WindowSystem.Refresh(target);
            if (!OperatingSystem.IsWindows()) return List().FirstOrDefault(window => window.Handle == target.Handle);
            var handle = target.Handle;
            if (NativeMethods.IsWindowVisible(handle) == 0 || NativeMethods.IsIconic(handle) != 0) return null;
            if (NativeMethods.DwmGetWindowAttribute(handle, 9, out var bounds, 16) != 0 &&
                NativeMethods.GetWindowRect(handle, out bounds) == 0) return null;
            if (GetClientRect(handle, out var client) == 0) return null;
            var origin = new NativeMethods.Point();
            if (ClientToScreen(handle, ref origin) == 0) return null;
            return target with
            {
                Bounds = bounds.Pixels, Client = new(origin.X, origin.Y, client.Right, client.Bottom),
            };
        }

        [LibraryImport("user32.dll", EntryPoint = "IsWindow")]
        private static partial int IsNativeWindow(nint window);

        internal static int IsWindow(nint window)
        {
            return OperatingSystem.IsWindows() ? IsNativeWindow(window) :
                List().Any(target => target.Handle == window) ? 1 : 0;
        }

        public static PixelRect? ResolveRegion(nint handle, WindowPreset preset)
        {
            if (!OperatingSystem.IsWindows())
            {
                var target = List().FirstOrDefault(window => window.Handle == handle);
                return target is null ? null : preset.Resolve(target.Bounds, target.Client);
            }

            if (NativeMethods.IsWindowVisible(handle) == 0 || NativeMethods.IsIconic(handle) != 0) return null;
            if (NativeMethods.DwmGetWindowAttribute(handle, 9, out var bounds, 16) != 0 &&
                NativeMethods.GetWindowRect(handle, out bounds) == 0) return null;
            if (GetClientRect(handle, out var client) == 0) return null;
            var origin = new NativeMethods.Point();
            if (ClientToScreen(handle, ref origin) == 0) return null;
            return preset.Resolve(bounds.Pixels, new(origin.X, origin.Y, client.Right, client.Bottom));
        }

        [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
        private static unsafe partial int GetWindowText(nint window, char* text, int count);

        [LibraryImport("user32.dll")]
        private static partial uint GetWindowThreadProcessId(nint window, out uint process);

        [LibraryImport("user32.dll")]
        private static partial int GetClientRect(nint window, out NativeMethods.Rect rect);

        [LibraryImport("user32.dll")]
        private static partial int ClientToScreen(nint window, ref NativeMethods.Point point);

        public static unsafe IReadOnlyList<WindowTarget> List()
        {
            if (OperatingSystem.IsMacOS()) return MacWindowSystem.List();
            if (OperatingSystem.IsLinux()) return DesktopCapture.UsesSourceSelection ? [] : X11WindowSystem.List();
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException(UiText.Get("PlatformUnsupported"));
            var result = new List<WindowTarget>();
            var processes = new Dictionary<uint, string>();
            var seen = new HashSet<nint>();
            for (var handle = NativeMethods.GetTopWindow(0);
                 handle != 0 && seen.Add(handle);
                 handle = NativeMethods.GetWindow(handle, 2))
            {
                if (NativeMethods.IsWindowVisible(handle) == 0 || NativeMethods.IsIconic(handle) != 0) continue;
                if (GetWindowThreadProcessId(handle, out var pid) == 0) continue;
                if (pid == Environment.ProcessId) continue;
                try
                {
                    if (!processes.TryGetValue(pid, out var name))
                    {
                        using var process = Process.GetProcessById((int)pid);
                        processes[pid] = name = process.ProcessName;
                    }

                    var title = new char[1024];
                    int length;
                    fixed (char* buffer = title) length = GetWindowText(handle, buffer, title.Length);

                    if (length == 0) continue;
                    if (NativeMethods.DwmGetWindowAttribute(handle, 9, out var bounds, 16) != 0 &&
                        NativeMethods.GetWindowRect(handle, out bounds) == 0) continue;
                    if (GetClientRect(handle, out var client) == 0) continue;
                    var origin = new NativeMethods.Point();
                    if (ClientToScreen(handle, ref origin) == 0) continue;
                    result.Add(new(handle, name, new(title, 0, length), bounds.Pixels,
                        new(origin.X, origin.Y, client.Right, client.Bottom)));
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
                {
                }
            }

            return result;
        }

        public static WindowTarget? Find(WindowPreset preset, IReadOnlyList<WindowTarget> windows, nint preferred = 0)
        {
            return windows.Where(w =>
                    WindowPreset.Matches(w.Process,
                        preset.Process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                            ? preset.Process[..^4]
                            : preset.Process) && WindowPreset.Matches(w.Title, preset.Title))
                .OrderByDescending(w => w.Handle == preferred).FirstOrDefault();
        }
    }
}

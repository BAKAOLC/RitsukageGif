using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class WindowControlLocator
    {
        // No messages are sent to the target process: a hung application cannot block hit testing.
        // HWND-less controls require a separate accessibility provider and are not represented here.
        public static WindowControlTarget? Find(WindowTarget window, PixelPoint point,
            CancellationToken cancellationToken = default)
        {
            if (!OperatingSystem.IsWindows() || !window.Bounds.Contains(point) ||
                cancellationToken.IsCancellationRequested) return null;
            var previousDpi = SetThreadDpiAwarenessContext(-4);
            try
            {
                if (NativeMethods.IsWindowVisible(window.Handle) == 0 ||
                    NativeMethods.IsIconic(window.Handle) != 0 ||
                    GetWindowThreadProcessId(window.Handle, out var process) == 0 ||
                    process == Environment.ProcessId) return null;

                var started = Stopwatch.GetTimestamp();
                var parent = window.Handle;
                var clip = window.Bounds;
                WindowControlTarget? result = null;
                var visited = new HashSet<nint>();
                for (var depth = 0; depth < 64; depth++)
                {
                    if (GetClientRect(parent, out var client) == 0) break;
                    var origin = new NativeMethods.Point();
                    if (ClientToScreen(parent, ref origin) == 0) break;
                    clip = clip.Intersect(new PixelRect(origin.X, origin.Y, client.Right, client.Bottom));
                    if (clip.IsEmpty || !clip.Contains(point)) break;
                    var found = false;
                    for (var child = NativeMethods.GetTopWindow(parent);
                         child != 0;
                         child = NativeMethods.GetWindow(child, 2))
                    {
                        if (cancellationToken.IsCancellationRequested) return null;
                        if (visited.Count >= 4096 || !visited.Add(child) ||
                            Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 8) return result;
                        if (GetParent(child) != parent || NativeMethods.IsWindowVisible(child) == 0 ||
                            NativeMethods.GetWindowRect(child, out var bounds) == 0) continue;
                        var visible = clip.Intersect(bounds.Pixels);
                        if (visible.IsEmpty || !visible.Contains(point)) continue;
                        result = new WindowControlTarget(window, child, visible);
                        parent = child;
                        clip = visible;
                        found = true;
                        break;
                    }

                    if (!found) break;
                }

                return result;
            }
            finally
            {
                if (previousDpi != 0) SetThreadDpiAwarenessContext(previousDpi);
            }
        }

        [LibraryImport("user32.dll")]
        private static partial nint SetThreadDpiAwarenessContext(nint context);

        [LibraryImport("user32.dll")]
        private static partial nint GetParent(nint window);

        [LibraryImport("user32.dll")]
        private static partial uint GetWindowThreadProcessId(nint window, out uint process);

        [LibraryImport("user32.dll")]
        private static partial int GetClientRect(nint window, out NativeMethods.Rect rect);

        [LibraryImport("user32.dll")]
        private static partial int ClientToScreen(nint window, ref NativeMethods.Point point);
    }
}

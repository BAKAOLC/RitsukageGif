using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RitsukageGif.Desktop
{
    internal static partial class X11WindowSystem
    {
        private const string Library = "libX11.so.6";

        private static readonly Lock Sync = new();
        private static nint _display;

        static X11WindowSystem() => AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            lock (Sync)
            {
                if (_display == 0) return;
                _ = XSync(_display, 0);
                X11ErrorTrap.Unregister(_display);
                _ = XCloseDisplay(_display);
                _display = 0;
            }
        };

        private static nint Display()
        {
            if (_display != 0) return _display;
            X11ErrorTrap.Initialize();
            _display = XOpenDisplay(0);
            if (_display == 0) throw new InvalidOperationException(UiText.Get("PlatformDisplayUnavailable"));
            X11ErrorTrap.Register(_display);
            return _display;
        }

        internal static WindowTarget? Refresh(WindowTarget target)
        {
            lock (Sync)
            {
                var display = Display();
                if (XGetWindowAttributes(display, target.Handle, out var attributes) == 0 || attributes.MapState != 2 ||
                    XTranslateCoordinates(display, target.Handle, XDefaultRootWindow(display), 0, 0, out var x,
                        out var y, out _) == 0) return null;
                var left = target.Client.X - target.Bounds.X;
                var top = target.Client.Y - target.Bounds.Y;
                var right = target.Bounds.Right - target.Client.Right;
                var bottom = target.Bounds.Bottom - target.Client.Bottom;
                return target with
                {
                    Client = new PixelRect(x, y, attributes.Width, attributes.Height),
                    Bounds = new PixelRect(x - left, y - top, attributes.Width + left + right,
                        attributes.Height + top + bottom)
                };
            }
        }

        internal static IReadOnlyList<WindowTarget> List()
        {
            lock (Sync)
            {
                var display = Display();
                var root = XDefaultRootWindow(display);
                var handles = Property(display, root, "_NET_CLIENT_LIST_STACKING", out var format);
                if (format != 32) handles = Property(display, root, "_NET_CLIENT_LIST", out format);
                if (format != 32) return [];
                var result = new List<WindowTarget>();
                for (var i = handles.Length - IntPtr.Size; i >= 0; i -= IntPtr.Size)
                {
                    var window = IntPtr.Size == 8
                        ? (nint)BitConverter.ToInt64(handles, i)
                        : BitConverter.ToInt32(handles, i);
                    if (XGetWindowAttributes(display, window, out var attributes) == 0 ||
                        attributes.MapState != 2) continue;
                    if (XTranslateCoordinates(display, window, root, 0, 0, out var x, out var y, out _) == 0) continue;
                    var processProperty = Property(display, window, "_NET_WM_PID", out var pidFormat);
                    var pid = pidFormat == 32 && processProperty.Length >= IntPtr.Size
                        ? BitConverter.ToInt32(processProperty)
                        : 0;
                    if (pid == Environment.ProcessId) continue;
                    var title = ReadText(display, window, "_NET_WM_NAME");
                    if (title.Length == 0) title = ReadText(display, window, "WM_NAME");
                    if (title.Length == 0) continue;
                    var process = "";
                    if (pid > 0)
                        try
                        {
                            using var owner = Process.GetProcessById(pid);
                            process = owner.ProcessName;
                        }
                        catch (Exception error) when (error is ArgumentException or InvalidOperationException
                                                          or System.ComponentModel.Win32Exception)
                        {
                        }

                    var client = new PixelRect(x, y, attributes.Width, attributes.Height);
                    var bounds = client;
                    var extents = Property(display, window, "_NET_FRAME_EXTENTS", out var extentsFormat);
                    if (extentsFormat == 32 && extents.Length >= 4 * IntPtr.Size)
                    {
                        var left = BitConverter.ToInt32(extents, 0);
                        var right = BitConverter.ToInt32(extents, IntPtr.Size);
                        var top = BitConverter.ToInt32(extents, IntPtr.Size * 2);
                        var bottom = BitConverter.ToInt32(extents, IntPtr.Size * 3);
                        if (left >= 0 && right >= 0 && top >= 0 && bottom >= 0 && left + right < 4096 &&
                            top + bottom < 4096)
                            bounds = new PixelRect(x - left, y - top, attributes.Width + left + right,
                                attributes.Height + top + bottom);
                    }

                    if (!bounds.IsEmpty) result.Add(new WindowTarget(window, process, title, bounds, client));
                }

                return result;
            }
        }

        private static string ReadText(nint display, nint window, string name)
        {
            var data = Property(display, window, name, out var format);
            return format == 8 ? Encoding.UTF8.GetString(data).TrimEnd('\0') : "";
        }

        private static byte[] Property(nint display, nint window, string name, out int format)
        {
            format = 0;
            var atom = XInternAtom(display, name, 1);
            if (atom == 0 || XGetWindowProperty(display, window, atom, 0, 65536, 0, 0,
                    out _, out format, out var count, out _, out var data) != 0) return [];
            try
            {
                var width = format == 32 ? IntPtr.Size : format / 8;
                if (width <= 0 || count > 65536) return [];
                var bytes = new byte[checked((int)count * width)];
                if (data != 0) Marshal.Copy(data, bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                if (data != 0) _ = XFree(data);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowAttributes
        {
            public int X, Y, Width, Height, BorderWidth, Depth;
            public nint Visual, Root;
            public int Class, BitGravity, WinGravity, BackingStore;
            public nuint BackingPlanes, BackingPixel;
            public int SaveUnder;
            public nint Colormap;
            public int MapInstalled, MapState;
            public nint AllEventMasks, YourEventMask, DoNotPropagateMask;
            public int OverrideRedirect;
            public nint Screen;
        }

        [LibraryImport(Library)]
        private static partial int XSync(nint display, int discard);

        [LibraryImport(Library)]
        private static partial nint XOpenDisplay(nint name);

        [LibraryImport(Library)]
        private static partial int XCloseDisplay(nint display);

        [LibraryImport(Library)]
        private static partial nint XDefaultRootWindow(nint display);

        [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint XInternAtom(nint display, string name, int onlyIfExists);

        [LibraryImport(Library)]
        private static partial int XGetWindowProperty(nint display, nint window, nint property, nint offset,
            nint length, int deleteProperty, nint requestedType, out nint actualType, out int format, out nuint count,
            out nuint remaining, out nint data);

        [LibraryImport(Library)]
        private static partial int XFree(nint data);

        [LibraryImport(Library)]
        private static partial int XGetWindowAttributes(nint display, nint window, out WindowAttributes attributes);

        [LibraryImport(Library)]
        private static partial int XTranslateCoordinates(nint display, nint source, nint destination, int x, int y,
            out int translatedX, out int translatedY, out nint child);
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class X11WindowSystem
    {
        internal static bool SupportsExternalTopmost()
        {
            lock (Sync)
                try
                {
                    var display = Display();
                    return TopmostAtomsSupported(display, XDefaultRootWindow(display));
                }
                catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException
                                                  or InvalidOperationException)
                {
                    return false;
                }
        }

        internal static Task<bool> ToggleTopmostAtPointAsync(Avalonia.PixelPoint point, CancellationToken token)
        {
            return Task.Run(() => ToggleTopmostAtPoint(point, token), token);
        }

        private static unsafe bool ToggleTopmostAtPoint(Avalonia.PixelPoint point, CancellationToken token)
        {
            X11ErrorTrap.Initialize();
            var display = XOpenDisplay(0);
            if (display == 0) throw new IOException("The X11 display is unavailable.");
            X11ErrorTrap.Register(display);
            try
            {
                var root = XDefaultRootWindow(display);
                if (!TopmostAtomsSupported(display, root))
                    throw new NotSupportedException(UiText.Get("GlobalActionTopmostUnavailable"));
                var window = FindTopmostClientAtPoint(display, root, point);
                if (window == 0)
                    throw new InvalidOperationException(UiText.Get("GlobalActionWindowAtPointerUnavailable"));
                X11ErrorTrap.Clear(display);
                if (XGetWindowAttributes(display, window, out var attributes) == 0)
                    throw new IOException("The target X11 window is no longer available.");
                var state = XInternAtom(display, "_NET_WM_STATE", 0);
                var above = XInternAtom(display, "_NET_WM_STATE_ABOVE", 0);
                var enabled = !ReadTopmostState(display, window, above);
                _ = XSelectInput(display, window, attributes.YourEventMask | (1 << 22) | (1 << 17));
                var message = stackalloc nint[24];
                new Span<nint>(message, 24).Clear();
                message[0] = 33;
                message[3] = display;
                message[4] = window;
                message[5] = state;
                message[6] = 32;
                message[7] = enabled ? 1 : 0;
                message[8] = above;
                message[10] = 2;
                token.ThrowIfCancellationRequested();
                if (XSendEvent(display, root, 0, (1 << 20) | (1 << 19), message) == 0)
                    throw new IOException("The X11 window manager rejected the topmost request.");
                _ = XSync(display, 0);
                if (X11ErrorTrap.HasError(display))
                    throw new IOException("The X11 topmost request failed.");
                var clock = Stopwatch.StartNew();
                var connection = new X11PollDescriptor { FileDescriptor = XConnectionNumber(display), Events = 1 };
                while (clock.ElapsedMilliseconds < 1500)
                {
                    token.ThrowIfCancellationRequested();
                    var current = ReadTopmostState(display, window, above);
                    if (X11ErrorTrap.HasError(display))
                        throw new IOException("The target X11 window is no longer available.");
                    if (current == enabled) return enabled;
                    var received = false;
                    while (XPending(display) > 0)
                    {
                        received = true;
                        _ = XNextEvent(display, message);
                        if ((int)message[0] == 17)
                            throw new IOException("The target X11 window closed before the request completed.");
                    }

                    if (received) continue;
                    var remaining = (int)Math.Min(100, 1500 - clock.ElapsedMilliseconds);
                    if (remaining > 0 && Poll(ref connection, 1, remaining) < 0)
                    {
                        if (Marshal.GetLastPInvokeError() == 4) continue;
                        throw new IOException("Waiting for the X11 window manager failed.");
                    }
                }

                throw new IOException("The X11 window manager did not apply the topmost request.");
            }
            finally
            {
                _ = XSync(display, 0);
                X11ErrorTrap.Unregister(display);
                _ = XCloseDisplay(display);
            }
        }

        private static bool TopmostAtomsSupported(nint display, nint root)
        {
            var supported = Property(display, root, "_NET_SUPPORTED", out var format);
            return format == 32 && ContainsAtom(supported, XInternAtom(display, "_NET_WM_STATE", 1)) &&
                   ContainsAtom(supported, XInternAtom(display, "_NET_WM_STATE_ABOVE", 1));
        }

        private static bool ReadTopmostState(nint display, nint window, nint above)
        {
            var state = Property(display, window, "_NET_WM_STATE", out var format);
            return format == 32 && ContainsAtom(state, above);
        }

        private static bool ContainsAtom(ReadOnlySpan<byte> atoms, nint expected)
        {
            if (expected == 0) return false;
            for (var index = 0; index + IntPtr.Size <= atoms.Length; index += IntPtr.Size)
            {
                var atom = IntPtr.Size == 8
                    ? (nint)BitConverter.ToInt64(atoms[index..])
                    : BitConverter.ToInt32(atoms[index..]);
                if (atom == expected) return true;
            }

            return false;
        }

        private static nint FindTopmostClientAtPoint(nint display, nint root, Avalonia.PixelPoint point)
        {
            var clients = Property(display, root, "_NET_CLIENT_LIST_STACKING", out var format);
            if (format != 32) return 0;
            for (var index = clients.Length - IntPtr.Size; index >= 0; index -= IntPtr.Size)
            {
                var window = IntPtr.Size == 8
                    ? (nint)BitConverter.ToInt64(clients, index)
                    : BitConverter.ToInt32(clients, index);
                if (XGetWindowAttributes(display, window, out var attributes) == 0 || attributes.MapState != 2 ||
                    XTranslateCoordinates(display, window, root, 0, 0, out var x, out var y, out _) == 0) continue;
                var extents = Property(display, window, "_NET_FRAME_EXTENTS", out var extentFormat);
                var left = extentFormat == 32 && extents.Length >= 4 * IntPtr.Size ? BitConverter.ToInt32(extents) : 0;
                var right = extentFormat == 32 && extents.Length >= 4 * IntPtr.Size
                    ? BitConverter.ToInt32(extents, IntPtr.Size)
                    : 0;
                var top = extentFormat == 32 && extents.Length >= 4 * IntPtr.Size
                    ? BitConverter.ToInt32(extents, 2 * IntPtr.Size)
                    : 0;
                var bottom = extentFormat == 32 && extents.Length >= 4 * IntPtr.Size
                    ? BitConverter.ToInt32(extents, 3 * IntPtr.Size)
                    : 0;
                if (point.X >= x - left && point.X < x + attributes.Width + right &&
                    point.Y >= y - top && point.Y < y + attributes.Height + bottom) return window;
            }

            return 0;
        }

        [LibraryImport(Library)]
        private static partial int XSelectInput(nint display, nint window, nint mask);

        [LibraryImport(Library)]
        private static unsafe partial int XSendEvent(nint display, nint window, int propagate, nint mask, nint* value);

        [LibraryImport(Library)]
        private static partial int XConnectionNumber(nint display);

        [LibraryImport(Library)]
        private static partial int XPending(nint display);

        [LibraryImport(Library)]
        private static unsafe partial int XNextEvent(nint display, nint* value);

        [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
        private static partial int Poll(ref X11PollDescriptor descriptor, nuint count, int timeout);
    }
}

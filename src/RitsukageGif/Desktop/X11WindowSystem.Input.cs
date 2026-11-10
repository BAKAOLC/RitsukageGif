using System.Runtime.InteropServices;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal static partial class X11WindowSystem
    {
        internal static KeyModifiers CurrentModifiers()
        {
            lock (Sync)
            {
                var display = Display();
                if (XQueryPointer(display, XDefaultRootWindow(display), out _, out _, out _, out _, out _, out _,
                        out var mask) == 0) return KeyModifiers.None;
                return ((mask & 1) != 0 ? KeyModifiers.Shift : 0) |
                       ((mask & 4) != 0 ? KeyModifiers.Control : 0) |
                       ((mask & 8) != 0 ? KeyModifiers.Alt : 0) |
                       ((mask & 64) != 0 ? KeyModifiers.Meta : 0);
            }
        }

        internal static Avalonia.PixelPoint? CurrentPointer()
        {
            lock (Sync)
            {
                var display = Display();
                return XQueryPointer(display, XDefaultRootWindow(display), out _, out _, out var x, out var y,
                    out _, out _, out _) == 0
                    ? null
                    : new Avalonia.PixelPoint(x, y);
            }
        }

        internal static unsafe (bool Held, bool Cancelled) CaptureRefreshInputState()
        {
            lock (Sync)
            {
                var display = Display();
                if (XQueryPointer(display, XDefaultRootWindow(display), out _, out _, out _, out _, out _, out _,
                        out var mask) == 0)
                    throw new IOException("X11 could not read the capture refresh pointer state.");
                var escape = XKeysymToKeycode(display, 0xFF1B);
                var keys = stackalloc byte[32];
                if (XQueryKeymap(display, keys) == 0)
                    throw new IOException("X11 could not read the capture refresh keyboard state.");
                return ((mask & 256) != 0, escape != 0 && (keys[escape / 8] & (1 << (escape % 8))) != 0);
            }
        }

        [LibraryImport(Library)]
        private static partial byte XKeysymToKeycode(nint display, nuint key);

        [LibraryImport(Library)]
        private static unsafe partial int XQueryKeymap(nint display, byte* keys);

        [LibraryImport(Library)]
        private static partial int XQueryPointer(nint display, nint window, out nint root, out nint child,
            out int rootX, out int rootY, out int windowX, out int windowY, out uint mask);
    }
}

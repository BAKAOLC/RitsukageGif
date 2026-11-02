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

        [LibraryImport(Library)]
        private static partial int XQueryPointer(nint display, nint window, out nint root, out nint child,
            out int rootX, out int rootY, out int windowX, out int windowY, out uint mask);
    }
}

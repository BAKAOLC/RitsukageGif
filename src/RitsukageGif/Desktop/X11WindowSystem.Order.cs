using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class X11WindowSystem
    {
        internal static void RaiseWithoutActivation(ReadOnlySpan<nint> bottomToTop)
        {
            lock (Sync)
            {
                var display = Display();
                var root = XDefaultRootWindow(display);
                foreach (var window in bottomToTop)
                {
                    var frame = window;
                    while (XQueryTree(display, frame, out _, out var parent, out var children, out _) != 0)
                    {
                        if (children != 0) _ = XFree(children);
                        if (parent == root || parent == 0) break;
                        frame = parent;
                    }

                    _ = XRaiseWindow(display, frame);
                }

                _ = XSync(display, 0);
            }
        }

        [LibraryImport(Library)]
        private static partial int XRaiseWindow(nint display, nint window);
    }
}

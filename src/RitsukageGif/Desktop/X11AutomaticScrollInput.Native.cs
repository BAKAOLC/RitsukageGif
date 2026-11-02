using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class X11AutomaticScrollInput
    {
        private const string X11 = "libX11.so.6";
        private const string XTest = "libXtst.so.6";

        [LibraryImport(X11)]
        private static partial nint XOpenDisplay(nint display);

        [LibraryImport(X11)]
        private static partial int XCloseDisplay(nint display);

        [LibraryImport(X11)]
        private static partial nint XDefaultRootWindow(nint display);

        [LibraryImport(X11)]
        private static partial int XSync(nint display, int discard);

        [LibraryImport(X11)]
        private static partial int XQueryPointer(nint display, nint window, out nint root,
            out nint child, out int rootX, out int rootY, out int windowX, out int windowY, out uint mask);

        [LibraryImport(X11, StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint XInternAtom(nint display, string name, int onlyIfExists);

        [LibraryImport(X11)]
        private static partial int XGetWindowProperty(nint display, nint window, nint property,
            nint offset, nint length, int deleteProperty, nint requestedType, out nint actualType, out int format,
            out nuint count, out nuint remaining, out nint data);

        [LibraryImport(X11)]
        private static partial int XFree(nint data);

        [LibraryImport(XTest)]
        private static partial int XTestQueryExtension(nint display,
            out int eventBase, out int errorBase, out int major, out int minor);

        [LibraryImport(XTest)]
        private static partial int XTestFakeButtonEvent(nint display, uint button, int pressed,
            nuint delay);
    }
}

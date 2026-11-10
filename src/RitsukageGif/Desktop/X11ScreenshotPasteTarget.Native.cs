using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class X11ScreenshotPasteTarget
    {
        private const string X11 = "libX11.so.6";
        private const string XTest = "libXtst.so.6";
        private const string XFixes = "libXfixes.so.3";

        [LibraryImport(X11)]
        private static partial nint XOpenDisplay(nint display);

        [LibraryImport(X11)]
        private static partial int XCloseDisplay(nint display);

        [LibraryImport(X11)]
        private static partial nint XDefaultRootWindow(nint display);

        [LibraryImport(X11)]
        private static partial int XSync(nint display, int discard);

        [LibraryImport(X11)]
        private static partial int XGetInputFocus(nint display, out nint focus, out int revert);

        [LibraryImport(X11)]
        private static partial int XQueryTree(nint display, nint window, out nint root, out nint parent,
            out nint children, out uint count);

        [LibraryImport(X11)]
        private static partial nint XCreateSimpleWindow(nint display, nint parent, int x, int y, uint width,
            uint height, uint borderWidth, nuint border, nuint background);

        [LibraryImport(X11)]
        private static partial int XDestroyWindow(nint display, nint window);

        [LibraryImport(X11, StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint XInternAtom(nint display, string name, int onlyIfExists);

        [LibraryImport(X11)]
        private static partial nint XGetSelectionOwner(nint display, nint selection);

        [LibraryImport(X11)]
        private static partial int XGetWindowProperty(nint display, nint window, nint property, nint offset,
            nint length, int deleteProperty, nint requestedType, out nint actualType, out int format,
            out nuint count, out nuint remaining, out nint data);

        [LibraryImport(X11)]
        private static partial int XFree(nint data);

        [LibraryImport(X11)]
        private static partial int XPending(nint display);

        [LibraryImport(X11)]
        private static partial int XNextEvent(nint display, nint eventBuffer);

        [LibraryImport(X11)]
        private static partial int XQueryKeymap(nint display, nint keys);

        [LibraryImport(X11)]
        private static partial byte XKeysymToKeycode(nint display, nuint symbol);

        [LibraryImport(XTest)]
        private static partial int XTestQueryExtension(nint display, out int eventBase, out int errorBase,
            out int major, out int minor);

        [LibraryImport(XTest)]
        private static partial int XTestFakeKeyEvent(nint display, uint keycode, int pressed, nuint delay);

        [LibraryImport(XFixes)]
        private static partial int XFixesQueryExtension(nint display, out int eventBase, out int errorBase);

        [LibraryImport(XFixes)]
        private static partial int XFixesQueryVersion(nint display, ref int major, ref int minor);

        [LibraryImport(XFixes)]
        private static partial void XFixesSelectSelectionInput(nint display, nint window, nint selection, nuint mask);
    }
}

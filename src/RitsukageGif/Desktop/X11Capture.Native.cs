using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class X11Capture
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Image
        {
            public int Width, Height, XOffset, Format;
            public nint Data;
            public int ByteOrder, BitmapUnit, BitmapBitOrder, BitmapPad, Depth, BytesPerLine, BitsPerPixel;
            public nuint RedMask, GreenMask, BlueMask;
            public nint ObjectData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Visual
        {
            public nint ExtensionData;
            public nuint Id;
            public int Class;
            public nuint RedMask, GreenMask, BlueMask;
            public int BitsPerRgb, MapEntries;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Attributes
        {
            public int X, Y, Width, Height, BorderWidth, Depth;
            public nint VisualPointer;
            public nuint Root;
            public int Class, BitGravity, WindowGravity, BackingStore;
            public nuint BackingPlanes, BackingPixel;
            public int SaveUnder;
            public nuint ColorMap;
            public int MapInstalled, MapState;
            public nint AllEventMasks, YourEventMask, DoNotPropagateMask;
            public int OverrideRedirect;
            public nint Screen;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorImage
        {
            public short X, Y;
            public ushort Width, Height, XHot, YHot;
            public nuint Serial;
            public nint Pixels;
            public nuint Atom;
            public nint Name;
        }

        [LibraryImport("libX11.so.6")]
        private static partial nint XOpenDisplay(nint name);

        [LibraryImport("libX11.so.6")]
        private static partial int XCloseDisplay(nint display);

        [LibraryImport("libX11.so.6")]
        private static partial nuint XDefaultRootWindow(nint display);

        [LibraryImport("libX11.so.6")]
        private static partial int XGetWindowAttributes(nint display, nuint window, out Attributes attributes);

        [LibraryImport("libX11.so.6")]
        private static partial nint XGetImage(nint display, nuint drawable, int x, int y, uint width, uint height,
            nuint planes, int format);

        [LibraryImport("libX11.so.6")]
        private static partial int XDestroyImage(nint image);

        [LibraryImport("libX11.so.6")]
        private static partial int XFreePixmap(nint display, nuint pixmap);

        [LibraryImport("libX11.so.6")]
        private static partial int XFree(nint pointer);

        [LibraryImport("libX11.so.6")]
        private static partial int XSync(nint display, int discard);

        [LibraryImport("libX11.so.6")]
        private static partial int XTranslateCoordinates(nint display, nuint source, nuint destination, int sourceX,
            int sourceY,
            out int destinationX, out int destinationY, out nuint child);

        [LibraryImport("libXcomposite.so.1")]
        private static partial int XCompositeQueryExtension(nint display, out int eventBase, out int errorBase);

        [LibraryImport("libXcomposite.so.1")]
        private static partial void XCompositeRedirectWindow(nint display, nuint window, int update);

        [LibraryImport("libXcomposite.so.1")]
        private static partial void XCompositeUnredirectWindow(nint display, nuint window, int update);

        [LibraryImport("libXcomposite.so.1")]
        private static partial nuint XCompositeNameWindowPixmap(nint display, nuint window);

        [LibraryImport("libXfixes.so.3")]
        private static partial nint XFixesGetCursorImage(nint display);
    }
}

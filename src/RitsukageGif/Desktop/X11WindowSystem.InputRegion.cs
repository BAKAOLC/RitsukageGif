using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class X11WindowSystem
    {
        private static bool? _inputRegionsAvailable;

        internal static bool CanSetInputRegion(nint window)
        {
            lock (Sync)
                try
                {
                    var display = Display();
                    X11ErrorTrap.Clear(display);
                    return XGetWindowAttributes(display, window, out _) != 0 && InputRegionsAvailable(display);
                }
                catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or
                                                  InvalidOperationException)
                {
                    return false;
                }
        }

        private static bool InputRegionsAvailable(nint display)
        {
            if (_inputRegionsAvailable is null)
            {
                var major = 2;
                var minor = 0;
                _inputRegionsAvailable = XFixesQueryExtension(display, out _, out _) != 0 &&
                                         XFixesQueryVersion(display, ref major, ref minor) != 0 && major >= 2;
            }

            return _inputRegionsAvailable == true;
        }

        internal static unsafe bool SetInputRegion(nint window, PinnedInputRegion? coverage, bool mouseThrough)
        {
            lock (Sync)
            {
                var display = Display();
                X11ErrorTrap.Clear(display);
                if (XGetWindowAttributes(display, window, out _) == 0) return false;
                if (!InputRegionsAvailable(display))
                    throw new NotSupportedException("The X11 server does not support XFixes input regions.");
                nint bitmap = 0;
                nint region = 0;
                try
                {
                    if (mouseThrough || coverage is not null)
                    {
                        if (mouseThrough || coverage!.Rectangles.IsEmpty)
                        {
                            region = XFixesCreateRegion(display, 0, 0);
                        }
                        else
                        {
                            var pixels = PinnedInputBitmapData.Pack(coverage);
                            fixed (byte* data = pixels)
                                bitmap = XCreateBitmapFromData(display, window, data,
                                    (uint)coverage.Size.Width, (uint)coverage.Size.Height);
                            if (bitmap == 0) throw new IOException("Unable to create the X11 input bitmap.");
                            region = XFixesCreateRegionFromBitmap(display, bitmap);
                        }

                        if (region == 0) throw new IOException("Unable to create the X11 input region.");
                    }

                    ApplyInputRegionTree(display, window, region);
                    _ = XSync(display, 0);
                    if (X11ErrorTrap.HasError(display)) throw new IOException("The X11 input-region update failed.");
                    return true;
                }
                finally
                {
                    if (region != 0) XFixesDestroyRegion(display, region);
                    if (bitmap != 0) _ = XFreePixmap(display, bitmap);
                    _ = XSync(display, 0);
                }
            }
        }

        private static unsafe void ApplyInputRegionTree(nint display, nint window, nint region)
        {
            var windows = new List<nint> { window };
            for (var index = 0; index < windows.Count; index++)
            {
                var current = windows[index];
                if (XTranslateCoordinates(display, current, window, 0, 0, out var x, out var y, out _) == 0)
                    continue;
                XFixesSetWindowShapeRegion(display, current, 2, -x, -y, region);
                if (XQueryTree(display, current, out _, out _, out var children, out var count) == 0) continue;
                try
                {
                    var pointers = (nint*)children;
                    for (var child = 0u; child < count; child++) windows.Add(pointers[child]);
                }
                finally
                {
                    if (children != 0) _ = XFree(children);
                }
            }
        }

        [LibraryImport("libXfixes.so.3")]
        private static partial int XFixesQueryExtension(nint display, out int eventBase, out int errorBase);

        [LibraryImport("libXfixes.so.3")]
        private static partial int XFixesQueryVersion(nint display, ref int major, ref int minor);

        [LibraryImport("libXfixes.so.3")]
        private static partial nint XFixesCreateRegion(nint display, nint rectangles, int count);

        [LibraryImport("libXfixes.so.3")]
        private static partial nint XFixesCreateRegionFromBitmap(nint display, nint bitmap);

        [LibraryImport("libXfixes.so.3")]
        private static partial void XFixesSetWindowShapeRegion(nint display, nint window, int kind, int x, int y,
            nint region);

        [LibraryImport("libXfixes.so.3")]
        private static partial void XFixesDestroyRegion(nint display, nint region);

        [LibraryImport(Library)]
        private static unsafe partial nint XCreateBitmapFromData(nint display, nint drawable, byte* data, uint width,
            uint height);

        [LibraryImport(Library)]
        private static partial int XFreePixmap(nint display, nint pixmap);

        [LibraryImport(Library)]
        private static partial int XQueryTree(nint display, nint window, out nint root, out nint parent,
            out nint children, out uint count);
    }
}

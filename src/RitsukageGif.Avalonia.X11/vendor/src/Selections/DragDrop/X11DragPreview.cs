using System;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using RitsukageGif.DragPreview;
using static Avalonia.X11.XLib;

// ReSharper disable once CheckNamespace
namespace Avalonia.X11.Selections.DragDrop
{
    internal sealed partial class X11DragPreview : IDisposable
    {
        private const int LsbFirst = 0;
        private const int ZPixmap = 2;
        private readonly IntPtr _display;
        private readonly IntPtr _pixmap;
        private readonly IntPtr _colormap;
        private readonly PixelPoint _hotSpot;
        private PixelPoint _position;

        private X11DragPreview(IntPtr display, IntPtr window, IntPtr pixmap, IntPtr colormap,
            PixelPoint hotSpot, PixelPoint position)
        {
            _display = display;
            Window = window;
            _pixmap = pixmap;
            _colormap = colormap;
            _hotSpot = hotSpot;
            _position = position;
        }

        public IntPtr Window { get; private set; }

        public void Dispose()
        {
            var window = Window;
            if (window == IntPtr.Zero) return;
            Window = IntPtr.Zero;
            Release(_display, window, _pixmap, _colormap);
            _ = XFlush(_display);
        }

        public static unsafe X11DragPreview? TryCreate(AvaloniaX11Platform platform, IDragPreview preview)
        {
            var info = platform.Info;
            var display = info.Display;
            if (!info.HasXFixes || !platform.Globals.IsCompositionEnabled ||
                info.TransparentVisualInfo is not
                    { red_mask: 0xff0000, green_mask: 0x00ff00, blue_mask: 0x0000ff } visual ||
                !XQueryExtension(display, "SHAPE", out _, out _, out _) ||
                !XQueryPointer(display, info.RootWindow, out _, out _, out var x, out var y, out _, out _, out _))
                return null;

            var size = preview.Preview.PixelSize;
            var pixmap = XCreatePixmap(display, info.RootWindow, size.Width, size.Height, 32);
            var colormap = IntPtr.Zero;
            var window = IntPtr.Zero;
            var region = IntPtr.Zero;
            var created = false;
            try
            {
                if (pixmap == IntPtr.Zero || !Upload(display, pixmap, visual, preview)) return null;
                colormap = XCreateColormap(display, info.RootWindow, visual.visual, 0);
                if (colormap == IntPtr.Zero) return null;
                var attributes = new XSetWindowAttributes
                {
                    background_pixmap = pixmap,
                    colormap = colormap,
                    override_redirect = 1,
                };
                const SetWindowValuemask mask = SetWindowValuemask.BackPixmap | SetWindowValuemask.BorderPixel |
                                                SetWindowValuemask.ColorMap | SetWindowValuemask.OverrideRedirect;
                window = XCreateWindow(display, info.RootWindow, x - preview.HotSpot.X, y - preview.HotSpot.Y,
                    size.Width, size.Height, 0, 32, (int)CreateWindowArgs.InputOutput,
                    visual.visual, new((uint)mask), ref attributes);
                if (window == IntPtr.Zero) return null;

                region = XFixesCreateRegion(display, null, 0);
                if (region == IntPtr.Zero) return null;
                XFixesSetWindowShapeRegion(display, window, ShapeKind.ShapeInput, 0, 0, region);
                if (!HasEmptyInputShape(display, window)) return null;
                var type = info.Atoms.GetAtom("_NET_WM_WINDOW_TYPE_DND");
                _ = XChangeProperty(display, window, info.Atoms._NET_WM_WINDOW_TYPE, info.Atoms.ATOM, 32,
                    PropertyMode.Replace, ref type, 1);
                _ = XMapWindow(display, window);
                _ = XRaiseWindow(display, window);
                created = true;
                return new(display, window, pixmap, colormap, preview.HotSpot, new(x, y));
            }
            finally
            {
                if (region != IntPtr.Zero) XFixesDestroyRegion(display, region);
                if (!created) Release(display, window, pixmap, colormap);
                _ = XFlush(display);
            }
        }

        private static bool HasEmptyInputShape(IntPtr display, IntPtr window)
        {
            var count = -1;
            var rectangles = XShapeGetRectangles(display, window, (int)ShapeKind.ShapeInput, ref count, out _);
            if (rectangles != IntPtr.Zero) _ = XFree(rectangles);
            return count == 0;
        }

        private static unsafe bool Upload(IntPtr display, IntPtr pixmap, XVisualInfo visual, IDragPreview preview)
        {
            var size = preview.Preview.PixelSize;
            var stride = size.Width * 4;
            var storage = new byte[size.Height * stride];
            fixed (byte* pixels = storage)
            {
                var address = (IntPtr)pixels;
                using var framebuffer = new LockedFramebuffer(address, size, stride, preview.Preview.Dpi,
                    PixelFormats.Bgra8888, AlphaFormat.Premul, () => { });
                preview.Preview.CopyPixels(framebuffer);
                var image = new XImage
                {
                    width = size.Width,
                    height = size.Height,
                    format = ZPixmap,
                    data = address,
                    byte_order = LsbFirst,
                    bitmap_unit = 32,
                    bitmap_bit_order = LsbFirst,
                    bitmap_pad = 32,
                    depth = 32,
                    bytes_per_line = stride,
                    bits_per_pixel = 32,
                    red_mask = (ulong)visual.red_mask,
                    green_mask = (ulong)visual.green_mask,
                    blue_mask = (ulong)visual.blue_mask,
                };
                if (XInitImage(ref image) == 0) return false;
                var gc = XCreateGC(display, pixmap, 0, IntPtr.Zero);
                if (gc == IntPtr.Zero) return false;
                try
                {
                    _ = XPutImage(display, pixmap, gc, ref image, 0, 0, 0, 0, (uint)size.Width, (uint)size.Height);
                    return true;
                }
                finally
                {
                    _ = XFreeGC(display, gc);
                }
            }
        }

        public void Move(PixelPoint position)
        {
            if (_position == position) return;
            _position = position;
            var changes = new XWindowChanges { x = position.X - _hotSpot.X, y = position.Y - _hotSpot.Y };
            _ = XConfigureWindow(_display, Window, ChangeWindowFlags.CWX | ChangeWindowFlags.CWY, ref changes);
        }

        private static void Release(IntPtr display, IntPtr window, IntPtr pixmap, IntPtr colormap)
        {
            if (window != IntPtr.Zero) _ = XDestroyWindow(display, window);
            if (pixmap != IntPtr.Zero) XFreePixmap(display, pixmap);
            if (colormap != IntPtr.Zero) _ = XFreeColormap(display, colormap);
        }

        [LibraryImport("libXext.so.6")]
        private static partial IntPtr XShapeGetRectangles(IntPtr display, IntPtr window, int kind,
            ref int count, out int ordering);
    }
}

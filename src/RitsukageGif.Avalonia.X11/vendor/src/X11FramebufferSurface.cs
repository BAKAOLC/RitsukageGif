using System;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using static Avalonia.X11.XLib;

namespace Avalonia.X11
{
    internal class X11FramebufferSurface : IFramebufferPlatformSurface
    {
        private readonly IntPtr _display;
        private readonly IntPtr _xid;
        private readonly int _depth;
        private readonly bool _retain;
        private RetainedFramebuffer? _fb;

        public X11FramebufferSurface(IntPtr display, IntPtr xid, int depth, bool retain)
        {
            _display = display;
            _xid = xid;
            _depth = depth;
            _retain = retain;
        }

        public IFramebufferRenderTarget CreateFramebufferRenderTarget()
        {
            return new FuncFramebufferRenderTarget(Lock, _retain);
        }

        private void Blit(RetainedFramebuffer fb)
        {
            var image = new XImage();
            var bitsPerPixel = 32;
            image.width = fb.Size.Width;
            image.height = fb.Size.Height;
            image.format = 2; //ZPixmap;
            image.data = fb.Address;
            image.byte_order = 0; // LSBFirst;
            image.bitmap_unit = bitsPerPixel;
            image.bitmap_bit_order = 0; // LSBFirst;
            image.bitmap_pad = bitsPerPixel;
            image.depth = _depth;
            image.bytes_per_line = fb.RowBytes;
            image.bits_per_pixel = bitsPerPixel;
            XLockDisplay(_display);
            XInitImage(ref image);
            var gc = XCreateGC(_display, _xid, 0, IntPtr.Zero);
            XPutImage(_display, _xid, gc, ref image, 0, 0, 0, 0, (uint)fb.Size.Width, (uint)fb.Size.Height);
            XFreeGC(_display, gc);
            XSync(_display, true);
            XUnlockDisplay(_display);
            if (!_retain)
            {
                _fb?.Dispose();
                _fb = null;
            }
        }

        public ILockedFramebuffer Lock(IRenderTarget.RenderTargetSceneInfo _, out FramebufferLockProperties properties)
        {
            XLockDisplay(_display);
            XGetGeometry(_display, _xid, out var root, out var x, out var y, out var width, out var height,
                out var bw, out var d);
            XUnlockDisplay(_display);

            var framebufferValid = _fb != null && _fb.Size.Width == width && _fb.Size.Height == height;
            if (!framebufferValid)
            {
                _fb?.Dispose();
                _fb = null;
                _fb = new(new(width, height), PixelFormat.Bgra8888, AlphaFormat.Premul);
            }

            properties = new(framebufferValid);
            return _fb!.Lock(new(96, 96), Blit);
        }
    }
}

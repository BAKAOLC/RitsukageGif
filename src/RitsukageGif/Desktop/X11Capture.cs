using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed unsafe partial class X11Capture : IDisposable
    {
        private readonly bool _cursor;
        private readonly Lock _gate = new();
        private readonly nuint _window, _root;
        private nint _display;
        private bool _redirected;

        public X11Capture(nint window, bool cursor)
        {
            _cursor = cursor;
            _window = (nuint)window;
            try
            {
                X11ErrorTrap.Initialize();
                _display = XOpenDisplay(0);
                if (_display == 0) throw new IOException(UiText.Get("CaptureFailed"));
                X11ErrorTrap.Register(_display);
                _root = XDefaultRootWindow(_display);
                if (_window != 0)
                {
                    if (XCompositeQueryExtension(_display, out _, out _) == 0)
                        throw new NotSupportedException(UiText.Get("WindowCaptureUnsupported"));
                    X11ErrorTrap.Clear(_display);
                    XCompositeRedirectWindow(_display, _window, 0);
                    _ = XSync(_display, 0);
                    if (X11ErrorTrap.HasError(_display)) throw new CaptureSourceLostException();
                    _redirected = true;
                }
            }
            catch (DllNotFoundException error)
            {
                Dispose();
                throw new NotSupportedException(UiText.Get("CaptureDependencyMissing"), error);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_display == 0) return;
                if (_redirected) XCompositeUnredirectWindow(_display, _window, 0);
                _ = XSync(_display, 0);
                X11ErrorTrap.Unregister(_display);
                _ = XCloseDisplay(_display);
                _display = 0;
                _redirected = false;
            }
        }

        public CaptureSourceFrame? CaptureScreen(PixelRect region)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_display == 0, this);
                X11ErrorTrap.Clear(_display);
                if (XGetWindowAttributes(_display, _root, out var root) == 0) throw CaptureError();
                var clipped = region.Intersect(new(0, 0, root.Width, root.Height));
                if (clipped.IsEmpty) return null;
                var frame = Read(_root, clipped.X, clipped.Y, clipped.Width, clipped.Height, false, root.VisualPointer);
                if (frame is null) throw CaptureError();
                if (_cursor) DrawCursor(frame.Pixels, frame.Width, frame.Height, clipped.X, clipped.Y);
                return frame with { OriginX = clipped.X, OriginY = clipped.Y };
            }
        }

        public CaptureSourceFrame? CaptureWindow()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_display == 0, this);
                X11ErrorTrap.Clear(_display);
                if (XGetWindowAttributes(_display, _window, out var attributes) == 0 || attributes.MapState != 2)
                {
                    _ = XSync(_display, 0);
                    return null;
                }

                var pixmap = XCompositeNameWindowPixmap(_display, _window);
                _ = XSync(_display, 0);
                if (pixmap == 0 || X11ErrorTrap.HasError(_display)) return null;
                try
                {
                    var frame = Read(pixmap, 0, 0, attributes.Width, attributes.Height, attributes.Depth == 32,
                        attributes.VisualPointer);
                    if (frame is not null && _cursor && XTranslateCoordinates(_display, _window, _root,
                            0, 0, out var x, out var y, out _) != 0)
                        DrawCursor(frame.Pixels, frame.Width, frame.Height, x, y);
                    return frame;
                }
                finally
                {
                    _ = XFreePixmap(_display, pixmap);
                    _ = XSync(_display, 0);
                }
            }
        }

        private CaptureSourceFrame? Read(nuint drawable, int x, int y, int width, int height, bool alpha, nint visual)
        {
            if (width <= 0 || height <= 0 || width > 32768 || height > 32768 ||
                (long)width * height * 4 > 512L * 1024 * 1024)
                throw new IOException(UiText.Get("InvalidFrameBuffer"));
            var image = XGetImage(_display, drawable, x, y, (uint)width, (uint)height, nuint.MaxValue, 2);
            _ = XSync(_display, 0);
            if (image == 0) return null;
            try
            {
                if (X11ErrorTrap.HasError(_display)) return null;
                var info = Marshal.PtrToStructure<Image>(image);
                if (info.Data == 0 || info.BitsPerPixel is not (8 or 16 or 24 or 32) ||
                    info.BytesPerLine < (long)width * (info.BitsPerPixel / 8))
                    throw new IOException(UiText.Get("InvalidFrameBuffer"));
                var red = (ulong)info.RedMask;
                var green = (ulong)info.GreenMask;
                var blue = (ulong)info.BlueMask;
                if ((red | green | blue) == 0 && visual != 0)
                {
                    var colors = Marshal.PtrToStructure<Visual>(visual);
                    red = colors.RedMask;
                    green = colors.GreenMask;
                    blue = colors.BlueMask;
                }

                if (red == 0 || green == 0 || blue == 0)
                    throw new IOException(UiText.Get("CapturePlatformUnsupported"));
                if ((long)info.BytesPerLine * height > 512L * 1024 * 1024)
                    throw new IOException(UiText.Get("InvalidFrameBuffer"));
                var pixels = X11PixelConverter.Convert(
                    new((void*)info.Data, checked(info.BytesPerLine * height)),
                    width, height, info.BytesPerLine, info.BitsPerPixel, info.ByteOrder == 0, red, green, blue, alpha);
                return new(width, height, pixels);
            }
            finally
            {
                _ = XDestroyImage(image);
            }
        }

        private void DrawCursor(byte[] pixels, int width, int height, int originX, int originY)
        {
            nint cursor;
            try
            {
                cursor = XFixesGetCursorImage(_display);
            }
            catch (DllNotFoundException)
            {
                throw new NotSupportedException(UiText.Get("CaptureDependencyMissing"));
            }

            if (cursor == 0) return;
            try
            {
                var image = Marshal.PtrToStructure<CursorImage>(cursor);
                if (image.Pixels == 0 || image.Width > 4096 || image.Height > 4096) return;
                var left = image.X - image.XHot - originX;
                var top = image.Y - image.YHot - originY;
                for (var row = 0; row < image.Height; row++)
                for (var col = 0; col < image.Width; col++)
                {
                    var x = left + col;
                    var y = top + row;
                    if (x < 0 || y < 0 || x >= width || y >= height) continue;
                    var value = (uint)((nuint*)image.Pixels)[row * image.Width + col];
                    var opacity = (byte)(value >> 24);
                    if (opacity == 0) continue;
                    var index = (y * width + x) * 4;
                    var backgroundAlpha = pixels[index + 3];
                    var resultAlpha = opacity + (backgroundAlpha * (255 - opacity) + 127) / 255;
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var premultiplied = (byte)(value >> (channel * 8));
                        var background = pixels[index + channel] * backgroundAlpha * (255 - opacity) / 255;
                        pixels[index + channel] = (byte)Math.Min(255, (premultiplied * 255 + background) / resultAlpha);
                    }

                    pixels[index + 3] = (byte)resultAlpha;
                }
            }
            finally
            {
                _ = XFree(cursor);
            }
        }

        private static IOException CaptureError()
        {
            return new(UiText.Get("CaptureFailed"));
        }
    }
}

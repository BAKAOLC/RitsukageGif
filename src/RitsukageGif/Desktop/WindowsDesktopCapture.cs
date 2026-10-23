using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed class WindowsDesktopCapture : IFrameSource
    {
        private readonly PixelRect[] _monitors;
        private readonly nint _pixels;
        private readonly nint _scaledPixels;
        private readonly int _width, _height;
        private RecordingOptions _options;
        private nint _scaledDc, _scaledBitmap, _scaledOld;
        private nint _screen, _memory, _bitmap, _old;
        private CaptureSegment[] _segments;
        private bool _disposed;

        public WindowsDesktopCapture(RecordingOptions options, IEnumerable<PixelRect>? monitors = null,
            int? outputWidth = null,
            int? outputHeight = null)
        {
            _options = options;
            _width = outputWidth ?? options.Width;
            _height = outputHeight ?? options.Height;
            if (_width <= 0 || _height <= 0 || (long)_width * _height * 4 > 512L * 1024 * 1024)
                throw new ArgumentException(UiText.Get("SelectionTooLarge"));
            _monitors = (monitors ?? [options.Region]).ToArray();
            options.Validate();
            _segments = ScreenComposition.Split(options.Region, _monitors);
            if (_segments.Length == 0) throw new ArgumentException(UiText.Get("NoMonitor"));
            if ((long)options.Region.Width * options.Region.Height * 4 > 512L * 1024 * 1024)
                throw new ArgumentException(UiText.Get("SelectionTooLarge"));
            try
            {
                _screen = NativeMethods.GetDC(0);
                if (_screen == 0) throw new CaptureSourceLostException();
                _memory = NativeMethods.CreateCompatibleDC(_screen);
                var info = new NativeMethods.BitmapInfo
                {
                    Size = 40, Width = options.Region.Width,
                    Height = -options.Region.Height, Planes = 1, BitCount = 32
                };
                _bitmap = NativeMethods.CreateDibSection(_screen, ref info, 0, out _pixels, 0, 0);
                if (_screen == 0 || _memory == 0 || _bitmap == 0)
                    throw new InvalidOperationException(UiText.Get("CaptureBufferFailed"));
                _old = NativeMethods.SelectObject(_memory, _bitmap);
                if (_width != options.Region.Width || _height != options.Region.Height)
                {
                    _scaledDc = NativeMethods.CreateCompatibleDC(_screen);
                    info.Width = _width;
                    info.Height = -_height;
                    _scaledBitmap = NativeMethods.CreateDibSection(_screen, ref info, 0, out _scaledPixels, 0, 0);
                    if (_scaledDc == 0 || _scaledBitmap == 0)
                        throw new InvalidOperationException(UiText.Get("ScaleBufferFailed"));
                    _scaledOld = NativeMethods.SelectObject(_scaledDc, _scaledBitmap);
                    NativeMethods.SetStretchBltMode(_scaledDc, 4); // HALFTONE area sampling
                    NativeMethods.SetBrushOrgEx(_scaledDc, 0, 0, 0);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public unsafe byte[] Capture()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_segments.Length == 0) throw new CaptureSourceLostException();
            var r = _options.Region;
            var data = new Span<byte>((void*)_pixels, checked(r.Width * r.Height * 4));
            data.Clear();
            foreach (var segment in _segments)
            {
                var source = segment.Source;
                if (NativeMethods.BitBlt(_memory, segment.DestinationX, segment.DestinationY, source.Width,
                        source.Height,
                        _screen, source.X, source.Y, 0x40cc0020) == 0)
                    throw new CaptureSourceLostException();
            }

            if (_options.Cursor) DrawCursor();
            if (_scaledDc != 0)
            {
                if (NativeMethods.StretchBlt(_scaledDc, 0, 0, _width, _height, _memory, 0, 0, r.Width,
                        r.Height, 0x00cc0020) == 0)
                    throw new InvalidOperationException(UiText.Get("ScaleFailed"));
                data = new Span<byte>((void*)_scaledPixels, checked(_width * _height * 4));
            }

            NativeMethods.GdiFlush(); // Complete batched GDI writes before reading DIB memory.
            for (var i = 3; i < data.Length; i += 4) data[i] = 255;
            return data.ToArray();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_scaledOld != 0)
            {
                NativeMethods.SelectObject(_scaledDc, _scaledOld);
                _scaledOld = 0;
            }

            if (_scaledBitmap != 0)
            {
                NativeMethods.DeleteObject(_scaledBitmap);
                _scaledBitmap = 0;
            }

            if (_scaledDc != 0)
            {
                NativeMethods.DeleteDC(_scaledDc);
                _scaledDc = 0;
            }

            if (_old != 0)
            {
                NativeMethods.SelectObject(_memory, _old);
                _old = 0;
            }

            if (_bitmap != 0)
            {
                NativeMethods.DeleteObject(_bitmap);
                _bitmap = 0;
            }

            if (_memory != 0)
            {
                NativeMethods.DeleteDC(_memory);
                _memory = 0;
            }

            if (_screen != 0)
            {
                NativeMethods.ReleaseDC(0, _screen);
                _screen = 0;
            }
        }

        public void Move(PixelRect region)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (region.IsEmpty || region.Width != _options.Region.Width || region.Height != _options.Region.Height)
                throw new ArgumentException(UiText.Get("SelectionTooLarge"));
            _options = _options with { Region = region };
            _segments = ScreenComposition.Split(region, _monitors);
        }

        private void DrawCursor()
        {
            var cursor = new NativeMethods.CursorInfo { Size = Marshal.SizeOf<NativeMethods.CursorInfo>() };
            if (NativeMethods.GetCursorInfo(ref cursor) == 0 || (cursor.Flags & 1) == 0) return;
            if (NativeMethods.GetIconInfo(cursor.Cursor, out var icon) == 0) return;
            try
            {
                NativeMethods.DrawIconEx(_memory, cursor.Position.X - _options.Region.X - (int)icon.XHotspot,
                    cursor.Position.Y - _options.Region.Y - (int)icon.YHotspot, cursor.Cursor, 0, 0, 0, 0, 3);
            }
            finally
            {
                if (icon.Color != 0) NativeMethods.DeleteObject(icon.Color);
                if (icon.Mask != 0) NativeMethods.DeleteObject(icon.Mask);
            }
        }
    }
}

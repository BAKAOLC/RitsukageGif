using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed class WindowsDesktopCapture : IFrameSource, IColorFrameSource
    {
        private readonly Dictionary<PixelRect, WindowsWindowContentCapture> _colorCaptures = [];
        private readonly Dictionary<PixelRect, WindowsWindowContentCapture> _hdrCaptures = [];
        private readonly PixelRect[] _monitors;
        private readonly nint _pixels;
        private readonly nint _scaledPixels;
        private readonly int _width, _height;
        private long _colorRevision;
        private bool _disposed;
        private RecordingOptions _options;
        private nint _scaledDc, _scaledBitmap, _scaledOld;
        private nint _screen, _memory, _bitmap, _old;
        private CaptureSegment[] _segments;

        public WindowsDesktopCapture(RecordingOptions options, IEnumerable<PixelRect>? monitors = null,
            int? outputWidth = null,
            int? outputHeight = null)
        {
            _options = options;
            _width = outputWidth ?? options.Width;
            _height = outputHeight ?? options.Height;
            if (_width <= 0 || _height <= 0 || (long)_width * _height * 4 > 512L * 1024 * 1024)
                throw new ArgumentException(UiText.Get("SelectionTooLarge"));
            _monitors = [.. monitors ?? [options.Region]];
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
                    Height = -options.Region.Height, Planes = 1, BitCount = 32,
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
                    if (NativeMethods.SetStretchBltMode(_scaledDc, 4) == 0 ||
                        NativeMethods.SetBrushOrgEx(_scaledDc, 0, 0, 0) == 0)
                        throw new InvalidOperationException(UiText.Get("ScaleBufferFailed"));
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public CapturedFrameSet CaptureFrames(CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            List<(PixelRect Bounds, CapturedFrameSet Frame)> sources = [];
            foreach (var segment in _segments)
            {
                token.ThrowIfCancellationRequested();
                var monitor = WindowsDisplayColor.Bounds(WindowsDisplayColor.ForRegion(segment.Source));
                if (sources.Any(item => item.Bounds == monitor)) continue;
                if (!_colorCaptures.TryGetValue(monitor, out var capture))
                {
                    capture = new(WindowsDisplayColor.ForRegion(monitor), _options.Cursor, true);
                    _colorCaptures.Add(monitor, capture);
                }

                sources.Add((monitor, capture.CaptureFrames(token)));
            }

            if (sources.Count == 0) throw new CaptureSourceLostException();
            return ColorFrameComposition.Compose(sources, _options.Region, _width, _height, ++_colorRevision, token);
        }

        public byte[] Capture()
        {
            return Capture(CancellationToken.None);
        }

        public unsafe byte[] Capture(CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_segments.Length == 0) throw new CaptureSourceLostException();
            var r = _options.Region;
            var data = new Span<byte>((void*)_pixels, checked(r.Width * r.Height * 4));
            data.Clear();
            foreach (var (source, destinationX, destinationY) in _segments)
            {
                token.ThrowIfCancellationRequested();
                var handle = WindowsDisplayColor.ForRegion(source);
                var monitor = WindowsDisplayColor.Bounds(handle);
                if (_hdrCaptures.TryGetValue(monitor, out var hdr) || WindowsDisplayColor.Read(handle).Hdr)
                {
                    if (hdr is null)
                    {
                        hdr = new(handle, false, true);
                        _hdrCaptures.Add(monitor, hdr);
                    }

                    var frame = hdr.Capture(token);
                    if (hdr.Width != monitor.Width || hdr.Height != monitor.Height)
                        throw new CaptureSourceLostException();
                    for (var row = 0; row < source.Height; row++)
                        frame.AsSpan(((source.Y - monitor.Y + row) * hdr.Width + source.X - monitor.X) * 4,
                                source.Width * 4)
                            .CopyTo(data.Slice(((destinationY + row) * r.Width + destinationX) * 4,
                                source.Width * 4));
                    continue;
                }

                if (NativeMethods.BitBlt(_memory, destinationX, destinationY, source.Width,
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
                data = new((void*)_scaledPixels, checked(_width * _height * 4));
            }

            if (NativeMethods.GdiFlush() == 0) throw new CaptureSourceLostException();
            for (var i = 3; i < data.Length; i += 4) data[i] = 255;
            return [.. data];
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var capture in _hdrCaptures.Values) capture.Dispose();
            _hdrCaptures.Clear();
            foreach (var capture in _colorCaptures.Values) capture.Dispose();
            _colorCaptures.Clear();
            if (_scaledOld != 0)
            {
                NativeMethods.SelectObject(_scaledDc, _scaledOld);
                _scaledOld = 0;
            }

            if (_scaledBitmap != 0)
            {
                ReportCleanup(NativeMethods.DeleteObject(_scaledBitmap), nameof(NativeMethods.DeleteObject));
                _scaledBitmap = 0;
            }

            if (_scaledDc != 0)
            {
                ReportCleanup(NativeMethods.DeleteDC(_scaledDc), nameof(NativeMethods.DeleteDC));
                _scaledDc = 0;
            }

            if (_old != 0)
            {
                NativeMethods.SelectObject(_memory, _old);
                _old = 0;
            }

            if (_bitmap != 0)
            {
                ReportCleanup(NativeMethods.DeleteObject(_bitmap), nameof(NativeMethods.DeleteObject));
                _bitmap = 0;
            }

            if (_memory != 0)
            {
                ReportCleanup(NativeMethods.DeleteDC(_memory), nameof(NativeMethods.DeleteDC));
                _memory = 0;
            }

            if (_screen != 0)
            {
                ReportCleanup(NativeMethods.ReleaseDC(0, _screen), nameof(NativeMethods.ReleaseDC));
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

        private static void ReportCleanup(int result, string operation)
        {
            if (result == 0) Trace.TraceWarning("Native capture cleanup failed: {0}", operation);
        }

        private void DrawCursor()
        {
            var cursor = new NativeMethods.CursorInfo { Size = Marshal.SizeOf<NativeMethods.CursorInfo>() };
            if (NativeMethods.GetCursorInfo(ref cursor) == 0 || (cursor.Flags & 1) == 0) return;
            if (NativeMethods.GetIconInfo(cursor.Cursor, out var icon) == 0) return;
            try
            {
                if (NativeMethods.DrawIconEx(_memory, cursor.Position.X - _options.Region.X - (int)icon.XHotspot,
                        cursor.Position.Y - _options.Region.Y - (int)icon.YHotspot, cursor.Cursor, 0, 0, 0, 0, 3) == 0)
                    Trace.TraceWarning("Could not draw the capture cursor.");
            }
            finally
            {
                if (icon.Color != 0)
                    ReportCleanup(NativeMethods.DeleteObject(icon.Color), nameof(NativeMethods.DeleteObject));
                if (icon.Mask != 0)
                    ReportCleanup(NativeMethods.DeleteObject(icon.Mask), nameof(NativeMethods.DeleteObject));
            }
        }
    }
}

using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed class PortableDesktopCapture : IFrameSource
    {
        private readonly X11Capture? _x11;

        private sealed class ScreenSource(PixelRect bounds)
        {
            public PixelRect Bounds { get; } = bounds;
            public NativeCaptureLease? Session;
            public long NextRetry;
        }

        private readonly List<ScreenSource> _screens = [];
        private readonly bool _cursor;
        private readonly int _fps;
        private readonly SKBitmap _canvas;
        private readonly SKCanvas _drawing;
        private readonly SKPaint _paint = new() { FilterQuality = SKFilterQuality.Medium, IsAntialias = true };
        private readonly Dictionary<int, SKBitmap> _bitmaps = [];
        private PixelRect _region;
        private bool _disposed;

        public PortableDesktopCapture(RecordingOptions options, PixelRect[] monitors, int width, int height)
        {
            options.Validate();
            if (width <= 0 || height <= 0 || (long)width * height * 4 > 512L * 1024 * 1024)
                throw new ArgumentException(UiText.Get("SelectionTooLarge"));
            _region = options.Region;
            _cursor = options.Cursor;
            _fps = options.Fps;
            _canvas = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            _drawing = new SKCanvas(_canvas);
            try
            {
                if (OperatingSystem.IsLinux() && !DesktopCapture.UsesSourceSelection)
                    _x11 = new X11Capture(0, options.Cursor);
                else if (OperatingSystem.IsMacOS())
                    foreach (var monitor in monitors)
                        _screens.Add(new ScreenSource(monitor));
                else if (!DesktopCapture.UsesSourceSelection)
                    throw new NotSupportedException(UiText.Get("CapturePlatformUnsupported"));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Move(PixelRect region)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (region.IsEmpty) throw new ArgumentException(UiText.Get("SelectionTooLarge"));
            _region = region;
        }

        public byte[] Capture() => Capture(default);

        public byte[] Capture(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            _drawing.Clear(SKColors.Transparent);
            var available = false;
            if (_x11 is not null)
            {
                if (_x11.CaptureScreen(_region) is { } frame)
                {
                    Draw(frame.Width, frame.Height, frame.Pixels, frame.Bounds, 0);
                    available = true;
                }
            }
            else if (DesktopCapture.UsesSourceSelection)
            {
                NativeCaptureSession.UseSharedFrame((frame, pixels) => Draw(frame.Width, frame.Height, pixels,
                    new PixelRect(0, 0, frame.Width, frame.Height), 0), token);
                available = true;
            }
            else
                for (var i = 0; i < _screens.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var source = _screens[i];
                    if (source.Bounds.Intersect(_region).IsEmpty || Environment.TickCount64 < source.NextRetry)
                        continue;
                    try
                    {
                        source.Session ??= NativeCaptureLease.Screen(source.Bounds, _cursor, _fps);
                        var index = i;
                        source.Session.UseFrame((frame, pixels) =>
                            Draw(frame.Width, frame.Height, pixels, source.Bounds, index), token);
                        available = true;
                    }
                    catch (CaptureSourceLostException)
                    {
                        token.ThrowIfCancellationRequested();
                        source.Session?.Dispose();
                        source.Session = null;
                        source.NextRetry = Environment.TickCount64 + 500;
                    }
                }

            token.ThrowIfCancellationRequested();
            if (!available) throw new CaptureSourceLostException();

            var result = new byte[checked(_canvas.Width * _canvas.Height * 4)];
            using var pixmap = _canvas.PeekPixels();
            unsafe
            {
                fixed (byte* address = result)
                    if (!pixmap.ReadPixels(_canvas.Info.WithAlphaType(SKAlphaType.Unpremul), (nint)address,
                            _canvas.Width * 4, 0, 0))
                        throw new IOException(UiText.Get("InvalidFrameBuffer"));
            }

            token.ThrowIfCancellationRequested();
            return result;
        }

        private unsafe void Draw(int width, int height, ReadOnlySpan<byte> pixels, PixelRect bounds, int index)
        {
            var clipped = bounds.Intersect(_region);
            if (clipped.IsEmpty) return;
            if (!_bitmaps.TryGetValue(index, out var bitmap) || bitmap.Width != width || bitmap.Height != height)
            {
                bitmap?.Dispose();
                _bitmaps[index] = bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            }

            pixels.CopyTo(new Span<byte>((void*)bitmap.GetPixels(), checked(width * height * 4)));
            bitmap.NotifyPixelsChanged();
            CaptureFrameDrawing.Draw(_drawing, _paint, bitmap, bounds, _region, _canvas.Width, _canvas.Height);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _x11?.Dispose();
            foreach (var screen in _screens) screen.Session?.Dispose();
            foreach (var bitmap in _bitmaps.Values) bitmap.Dispose();
            _paint.Dispose();
            _drawing.Dispose();
            _canvas.Dispose();
        }
    }
}

using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed class WindowContentCapture : IColorFrameSource
    {
        private readonly NativeCaptureLease? _native;
        private readonly WindowsWindowContentCapture? _windows;
        private readonly X11Capture? _x11;
        private int _width, _height;

        public WindowContentCapture(nint window, bool cursor, int fps = 60)
        {
            if (OperatingSystem.IsWindows()) _windows = new(window, cursor);
            else if (OperatingSystem.IsMacOS())
                _native = NativeCaptureLease.Window(window, cursor, fps);
            else if (OperatingSystem.IsLinux() && !DesktopCapture.UsesSourceSelection)
                _x11 = new(window, cursor);
            else throw new NotSupportedException(UiText.Get("CapturePlatformUnsupported"));
        }

        public int Width => _windows?.Width ?? _width;
        public int Height => _windows?.Height ?? _height;
        public long FrameRevision => _windows?.FrameRevision ?? -1;

        public void Dispose()
        {
            _windows?.Dispose();
            _x11?.Dispose();
            _native?.Dispose();
        }

        public CapturedFrameSet CaptureFrames(CancellationToken token = default)
        {
            if (_windows is not null) return _windows.CaptureFrames(token);
            var pixels = Capture(token) ?? throw new CaptureSourceLostException();
            var color = new FrameColorInfo(false);
            return new(new(Width, Height, checked(Width * 4), CapturePixelFormat.Bgra8Srgb, pixels.ToArray(), color,
                    CaptureAlphaMode.Opaque, FrameRevision, [new(new(0, 0, Width, Height), color)]), null,
                Stopwatch.GetTimestamp());
        }

        public byte[]? Capture(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (_windows is not null)
            {
                var pixels = _windows.Capture(token);
                token.ThrowIfCancellationRequested();
                return pixels;
            }

            var frame = _x11?.CaptureWindow() ?? _native?.Read(token);
            token.ThrowIfCancellationRequested();

            if (frame is null) return null;
            _width = frame.Width;
            _height = frame.Height;
            return frame.Pixels;
        }
    }
}

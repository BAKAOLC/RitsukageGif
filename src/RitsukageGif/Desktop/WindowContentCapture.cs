namespace RitsukageGif.Desktop
{
    internal sealed class WindowContentCapture : IDisposable
    {
        private readonly WindowsWindowContentCapture? _windows;
        private readonly X11Capture? _x11;
        private readonly NativeCaptureLease? _native;
        private int _width, _height;
        public int Width => _windows?.Width ?? _width;
        public int Height => _windows?.Height ?? _height;
        public long FrameRevision => _windows?.FrameRevision ?? -1;

        public WindowContentCapture(nint window, bool cursor, int fps = 60)
        {
            if (OperatingSystem.IsWindows()) _windows = new WindowsWindowContentCapture(window, cursor);
            else if (OperatingSystem.IsMacOS())
                _native = NativeCaptureLease.Window(window, cursor, fps);
            else if (OperatingSystem.IsLinux() && !DesktopCapture.UsesSourceSelection)
                _x11 = new X11Capture(window, cursor);
            else throw new NotSupportedException(UiText.Get("CapturePlatformUnsupported"));
        }

        public byte[]? Capture() => Capture(default);

        public byte[]? Capture(CancellationToken token)
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

        public void Dispose()
        {
            _windows?.Dispose();
            _x11?.Dispose();
            _native?.Dispose();
        }
    }
}

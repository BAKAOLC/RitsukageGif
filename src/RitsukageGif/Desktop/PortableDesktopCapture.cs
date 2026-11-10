using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed class PortableDesktopCapture : IFrameSource, IColorFrameSource
    {
        private readonly int _width, _height, _fps;
        private readonly bool _cursor;
        private readonly List<ScreenSource> _screens = [];
        private readonly X11Capture? _x11;
        private readonly NativeCaptureDemand? _sharedDemand;
        private PortableFrameComposer _composer;
        private bool _disposed, _retainHdr;
        private PixelRect _region;
        private readonly string? _includedWindowTitle;

        public PortableDesktopCapture(RecordingOptions options, PixelRect[] monitors, int width, int height,
            NativeCaptureSession? nativeSource = null, string? includedWindowTitle = null)
        {
            options.Validate();
            if (width <= 0 || height <= 0 || (long)width * height * 4 > 512L * 1024 * 1024)
                throw new ArgumentException(UiText.Get("SelectionTooLarge"));
            _region = options.Region;
            _includedWindowTitle = includedWindowTitle;
            _cursor = options.Cursor;
            _fps = options.Fps;
            _width = width;
            _height = height;
            _composer = new(width, height, false);
            try
            {
                if (DesktopCapture.UsesSourceSelection)
                    _sharedDemand = new(nativeSource?.Retain() ?? NativeCaptureSession.RetainShared());
                else if (OperatingSystem.IsLinux() && !DesktopCapture.UsesSourceSelection)
                    _x11 = new(0, options.Cursor);
                else if (OperatingSystem.IsMacOS())
                    foreach (var monitor in monitors)
                        _screens.Add(new(monitor));
                else if (!DesktopCapture.UsesSourceSelection)
                    throw new NotSupportedException(UiText.Get("CapturePlatformUnsupported"));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public CapturedFrameSet CaptureFrames(CancellationToken token = default)
        {
            if (!_retainHdr)
            {
                if ((long)_width * _height * 12 > 512L * 1024 * 1024)
                    throw new ArgumentException(UiText.Get("SelectionTooLarge"));
                _retainHdr = true;
                _composer.Dispose();
                _composer = new(_width, _height, true);
                foreach (var screen in _screens)
                {
                    screen.Session?.Dispose();
                    screen.Session = null;
                }
            }

            return CaptureCore(token);
        }

        public byte[] Capture()
        {
            return Capture(CancellationToken.None);
        }

        public byte[] Capture(CancellationToken token)
        {
            return CapturedFramePixels.ToStraightBgra(CaptureCore(token).Sdr, token);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _x11?.Dispose();
            _sharedDemand?.Dispose();
            foreach (var screen in _screens) screen.Session?.Dispose();
            _composer.Dispose();
        }

        private CapturedFrameSet CaptureCore(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            _composer.Begin();
            var available = false;
            if (_x11 is not null)
            {
                if (_x11.CaptureScreen(_region) is { } frame)
                {
                    var color = new FrameColorInfo(false);
                    var sdr = new CapturedFrame(frame.Width, frame.Height, frame.Width * 4,
                        CapturePixelFormat.Bgra8Srgb,
                        frame.Pixels, color, CaptureAlphaMode.Opaque, -1, []);
                    _composer.Draw(new(new(sdr, null, Stopwatch.GetTimestamp()),
                        new(), null, null), frame.Bounds, _region, 0, token: token);
                    available = true;
                }
            }
            else if (DesktopCapture.UsesSourceSelection)
            {
                _sharedDemand!.Use(value =>
                        _composer.Draw(value, value.Geometry.Bounds, _region, 0, _cursor, token), _region,
                    Math.Max((double)_width / _region.Width, (double)_height / _region.Height), false, _fps,
                    _retainHdr, token);
                available = true;
            }
            else
            {
                for (var i = 0; i < _screens.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var source = _screens[i];
                    if (source.Bounds.Intersect(_region).IsEmpty || Environment.TickCount64 < source.NextRetry)
                        continue;
                    try
                    {
                        source.Session ??= NativeCaptureLease.Screen(source.Bounds, _cursor, _fps, _retainHdr,
                            _includedWindowTitle);
                        var index = i;
                        source.Session.UseSnapshot(
                            value => _composer.Draw(value, value.Geometry.Bounds, _region, index, _cursor, token),
                            false,
                            source.Bounds.Intersect(_region), Math.Max((double)_width / _region.Width,
                                (double)_height / _region.Height), token);
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
            }

            if (!available) throw new CaptureSourceLostException();
            return _composer.Finish(token);
        }

        public void Move(PixelRect region)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (region.IsEmpty) throw new ArgumentException(UiText.Get("SelectionTooLarge"));
            _region = region;
        }

        internal void Suspend()
        {
            _sharedDemand?.Suspend();
            foreach (var source in _screens) source.Session?.SuspendDemand();
        }

        private sealed class ScreenSource(PixelRect bounds)
        {
            public long NextRetry;
            public NativeCaptureLease? Session;
            public PixelRect Bounds { get; } = bounds;
        }
    }
}

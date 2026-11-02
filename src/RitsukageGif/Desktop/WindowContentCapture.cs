using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed class WindowContentCapture : IColorFrameSource
    {
        private NativeCaptureLease? _native;
        private PortableFrameComposer? _portalComposer;
        private int _portalWidth, _portalHeight;
        private bool _portalHdr;
        private readonly bool _usesNative;
        private readonly NativeCaptureDemand? _portal;
        private bool _nativeHdr;
        private readonly bool _retainFrames;
        private readonly bool _cursor;
        private readonly int _fps;
        private readonly nint _handle;
        private readonly WindowsWindowContentCapture? _windows;
        private readonly X11Capture? _x11;
        private int _width, _height;
        private PixelRect? _requestedRegion;
        private double? _requestedDensity;

        public WindowContentCapture(nint window, bool cursor, int fps = 60, bool retainFrames = true,
            NativeCaptureAuthorization? authorization = null)
        {
            _retainFrames = retainFrames;
            _cursor = cursor;
            _fps = fps;
            _handle = window;
            if (OperatingSystem.IsWindows()) _windows = new(window, cursor, retainFrames: retainFrames);
            else if (OperatingSystem.IsMacOS())
                _usesNative = true;
            else if (OperatingSystem.IsLinux() && !DesktopCapture.UsesSourceSelection)
                _x11 = new(window, cursor);
            else if (DesktopCapture.UsesSourceSelection && window == -1 && NativeCaptureSession.SourceIsWindow)
                _portal = new(() => NativeCaptureSession.CurrentAuthorizedWindow(authorization));
            else
                throw new NotSupportedException(UiText.Get(DesktopCapture.UsesSourceSelection
                    ? "PortalWindowMatchingUnavailable"
                    : "CapturePlatformUnsupported"));
        }

        public PixelRect? CoordinateBounds { get; private set; }
        internal double NativePixelDensity { get; private set; } = 1;

        public int Width => _windows?.Width ?? _width;
        public int Height => _windows?.Height ?? _height;
        public long FrameRevision => _windows?.FrameRevision ?? -1;

        public void Dispose()
        {
            _windows?.Dispose();
            _x11?.Dispose();
            _native?.Dispose();
            _portal?.Dispose();
            _portalComposer?.Dispose();
        }

        public CapturedFrameSet CaptureFrames(CancellationToken token = default)
        {
            if (_windows is not null) return _windows.CaptureFrames(token);
            if (_portal is not null)
            {
                CapturedFrameSet? result = null;
                _portal.Use(value =>
                {
                    _width = value.Frames.Sdr.Width;
                    _height = value.Frames.Sdr.Height;
                    CoordinateBounds = value.Geometry.Bounds;
                    NativePixelDensity = value.Geometry.BackingScale;
                    result = ComposeNativeFrame(value, value.Frames.Hdr is not null, token);
                }, _requestedRegion, _requestedDensity, false, token);
                return result!;
            }

            if (_usesNative)
            {
                if (!_nativeHdr)
                {
                    _native?.Dispose();
                    _native = NativeCaptureLease.Window(_handle, _cursor, _fps, true);
                    _nativeHdr = true;
                }

                var snapshot = _native!.ReadSnapshot(false, _requestedRegion, _requestedDensity, token);
                _width = snapshot.Frames.Sdr.Width;
                _height = snapshot.Frames.Sdr.Height;
                CoordinateBounds = snapshot.Geometry.Bounds;
                NativePixelDensity = snapshot.Geometry.BackingScale;
                return ComposeNativeFrame(snapshot, snapshot.Frames.Hdr is not null, token);
            }

            var pixels = Capture(token) ?? throw new CaptureSourceLostException();
            var color = new FrameColorInfo(false);
            return new(new(Width, Height, checked(Width * 4), CapturePixelFormat.Bgra8Srgb,
                    _retainFrames ? [.. pixels] : pixels, color,
                    CaptureAlphaMode.Opaque, FrameRevision, [new(new(0, 0, Width, Height), color)]), null,
                Stopwatch.GetTimestamp());
        }

        internal void ConfigureRegion(PixelRect region, int outputWidth, int outputHeight)
        {
            if (!_usesNative && _portal is null) return;
            _requestedRegion = region;
            _requestedDensity = Math.Max((double)outputWidth / region.Width, (double)outputHeight / region.Height);
        }

        internal void ConfigureNativeRegion(PixelRect region)
        {
            if (!_usesNative && _portal is null) return;
            _requestedRegion = region;
            _requestedDensity = null;
        }

        internal void Suspend()
        {
            _native?.SuspendDemand();
            _portal?.Dispose();
        }

        private CapturedFrameSet ComposeNativeFrame(NativeCaptureSnapshot value, bool hdr, CancellationToken token)
        {
            if (_portalComposer is null || _portalWidth != _width || _portalHeight != _height || _portalHdr != hdr)
            {
                _portalComposer?.Dispose();
                _portalComposer = new(_width, _height, hdr);
                _portalWidth = _width;
                _portalHeight = _height;
                _portalHdr = hdr;
            }

            _portalComposer.Begin();
            _portalComposer.Draw(value, value.Geometry.Bounds, value.Geometry.Bounds, 0, _cursor, token);
            return _portalComposer.Finish(token);
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

            if (_portal is not null)
            {
                byte[]? pixels = null;
                _portal.Use(value =>
                {
                    _width = value.Frames.Sdr.Width;
                    _height = value.Frames.Sdr.Height;
                    CoordinateBounds = value.Geometry.Bounds;
                    NativePixelDensity = value.Geometry.BackingScale;
                    pixels = CapturedFramePixels.ToStraightBgra(ComposeNativeFrame(value, false, token).Sdr, token);
                }, _requestedRegion, _requestedDensity, token: token);
                return pixels;
            }

            if (_usesNative)
            {
                _native ??= NativeCaptureLease.Window(_handle, _cursor, _fps);
                byte[]? pixels = null;
                _native.UseSnapshot(value =>
                {
                    _width = value.Frames.Sdr.Width;
                    _height = value.Frames.Sdr.Height;
                    CoordinateBounds = value.Geometry.Bounds;
                    NativePixelDensity = value.Geometry.BackingScale;
                    pixels = CapturedFramePixels.ToStraightBgra(ComposeNativeFrame(value, false, token).Sdr, token);
                }, region: _requestedRegion, density: _requestedDensity, token: token);
                return pixels;
            }

            var frame = _x11?.CaptureWindow();
            token.ThrowIfCancellationRequested();

            if (frame is null) return null;
            _width = frame.Width;
            CoordinateBounds = frame.Bounds;
            NativePixelDensity = frame.PixelDensity;
            _height = frame.Height;
            return frame.Pixels;
        }
    }
}

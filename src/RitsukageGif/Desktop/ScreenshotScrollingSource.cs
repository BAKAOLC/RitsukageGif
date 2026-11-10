namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotScrollingSource : IDisposable
    {
        private readonly DesktopCapture? _desktop;
        private readonly WindowTarget? _target;
        private readonly NativeCaptureSession? _nativeSource;
        private readonly HdrToneMapMode _toneMap;
        private readonly double _density;
        private bool _colorUnavailable;
        private PixelRect _current;
        private WindowContentCapture? _window;

        public ScreenshotScrollingSource(PixelRect region, PixelRect[] monitors, WindowTarget? target,
            HdrToneMapMode toneMap = HdrToneMapMode.HighlightCompression, double pixelDensity = 1,
            NativeCaptureSession? nativeSource = null)
        {
            _toneMap = toneMap;
            _density = pixelDensity;
            _target = target;
            _nativeSource = nativeSource;
            _current = region;
            if (target is null)
                _desktop = new(new(region, Divisor: 1) { PixelDensity = _density }, monitors,
                    nativeSource: nativeSource);
        }

        public void Dispose()
        {
            _desktop?.Dispose();
            _window?.Dispose();
        }

        internal void Suspend()
        {
            _desktop?.Suspend();
            _window?.Suspend();
        }

        public byte[] Capture(PixelRect region, CancellationToken token)
        {
            if (_desktop is not null)
            {
                if (_current != region)
                {
                    _desktop.Move(region);
                    _current = region;
                }

                if (!_colorUnavailable && _toneMap != HdrToneMapMode.HighlightCompression)
                    try
                    {
                        return ReadSdr(_desktop.CaptureFrames(token), token);
                    }
                    catch (Exception error) when (SelectionCoordinator.IsOptionalHdrUnsupported(error))
                    {
                        token.ThrowIfCancellationRequested();
                        _colorUnavailable = true;
                    }

                return _desktop.Capture(token);
            }

            var target = RefreshTarget(_target!) ?? throw new CaptureSourceLostException();
            var bounds = OperatingSystem.IsLinux() ? target.Client : target.Bounds;
            if (region.Intersect(target.Client) != region) throw new CaptureSourceLostException();
            _window ??= new(target.Handle, false, 10, authorization: target.NativeAuthorization,
                nativeSource: _nativeSource);
            _window.ConfigureRegion(region, Math.Max(1, (int)Math.Ceiling(region.Width * _density)),
                Math.Max(1, (int)Math.Ceiling(region.Height * _density)));
            byte[]? pixels = null;
            if (!_colorUnavailable && _toneMap != HdrToneMapMode.HighlightCompression)
                try
                {
                    pixels = ReadSdr(_window.CaptureFrames(token), token);
                }
                catch (Exception error) when (SelectionCoordinator.IsOptionalHdrUnsupported(error))
                {
                    token.ThrowIfCancellationRequested();
                    _colorUnavailable = true;
                    _window.Dispose();
                    _window = new(target.Handle, false, 10, authorization: target.NativeAuthorization,
                        nativeSource: _nativeSource);
                }

            pixels ??= _window.Capture(token) ?? throw new CaptureSourceLostException();
            var refreshed = RefreshTarget(target) ?? throw new CaptureSourceLostException();
            if (target.Client != refreshed.Client ||
                bounds != (OperatingSystem.IsLinux() ? refreshed.Client : refreshed.Bounds))
                throw new CaptureSourceLostException();
            return Crop(pixels, _window.Width, _window.Height, _window.CoordinateBounds ?? bounds, region, _density,
                token);
        }

        private byte[] ReadSdr(CapturedFrameSet frames, CancellationToken token)
        {
            return CapturedFramePixels.ToStraightBgra(frames.Hdr is { } hdr
                ? HdrToneMapping.Map(hdr, _toneMap, token)
                : frames.Sdr, token);
        }

        private WindowTarget? RefreshTarget(WindowTarget target)
        {
            return _nativeSource is null
                ? WindowLocator.Refresh(target)
                : _nativeSource.GetAuthorizedWindows().FirstOrDefault(window => window.Handle == target.Handle &&
                    window.NativeSourceIdentity == target.NativeSourceIdentity);
        }

        private static byte[] Crop(byte[] pixels, int width, int height, PixelRect bounds, PixelRect region,
            double density, CancellationToken token)
        {
            if (width <= 0 || height <= 0 || pixels.LongLength != (long)width * height * 4 ||
                bounds.IsEmpty || region.IsEmpty || region.Intersect(bounds) != region)
                throw new CaptureSourceLostException();
            if (density.Equals(Math.Truncate(density)) && ((double)width).Equals(bounds.Width * density) &&
                ((double)height).Equals(bounds.Height * density))
            {
                var outputWidth = checked((int)(region.Width * density));
                var outputHeight = checked((int)(region.Height * density));
                var left = checked((int)((region.X - bounds.X) * density));
                var top = checked((int)((region.Y - bounds.Y) * density));
                var cropped = new byte[checked(outputWidth * outputHeight * 4)];
                for (var y = 0; y < outputHeight; y++)
                {
                    token.ThrowIfCancellationRequested();
                    pixels.AsSpan(((top + y) * width + left) * 4, outputWidth * 4)
                        .CopyTo(cropped.AsSpan(y * outputWidth * 4));
                }

                return cropped;
            }

            var frame = new CapturedFrame(width, height, checked(width * 4), CapturePixelFormat.Bgra8Srgb,
                pixels, new(false), CaptureAlphaMode.Straight, 0, []);
            var output = NativeFrameComposition.Compose([(bounds, new(frame, null, 0))], region, density, token);
            return CapturedFramePixels.ToStraightBgra(output.Sdr, token);
        }
    }
}

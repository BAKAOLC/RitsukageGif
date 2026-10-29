using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed class ScreenshotScrollingSource : IDisposable
    {
        private readonly DesktopCapture? _desktop;
        private readonly WindowTarget? _target;
        private readonly HdrToneMapMode _toneMap;
        private bool _colorUnavailable;
        private PixelRect _current;
        private WindowContentCapture? _window;

        public ScreenshotScrollingSource(PixelRect region, PixelRect[] monitors, WindowTarget? target,
            HdrToneMapMode toneMap = HdrToneMapMode.HighlightCompression)
        {
            _toneMap = toneMap;
            _target = target;
            _current = region;
            if (target is null) _desktop = new(new(region, Divisor: 1), monitors);
        }

        public void Dispose()
        {
            _desktop?.Dispose();
            _window?.Dispose();
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

            var target = WindowLocator.Refresh(_target!) ?? throw new CaptureSourceLostException();
            var bounds = OperatingSystem.IsLinux() ? target.Client : target.Bounds;
            if (region.Intersect(target.Client) != region) throw new CaptureSourceLostException();
            _window ??= new(target.Handle, false, 10);
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
                    _window = new(target.Handle, false, 10);
                }

            pixels ??= _window.Capture(token) ?? throw new CaptureSourceLostException();
            var refreshed = WindowLocator.Refresh(target) ?? throw new CaptureSourceLostException();
            if (target.Client != refreshed.Client ||
                bounds != (OperatingSystem.IsLinux() ? refreshed.Client : refreshed.Bounds))
                throw new CaptureSourceLostException();
            return Crop(pixels, _window.Width, _window.Height, bounds, region, token);
        }

        private byte[] ReadSdr(CapturedFrameSet frames, CancellationToken token)
        {
            return CapturedFramePixels.ToStraightBgra(frames.Hdr is { } hdr
                ? HdrToneMapping.Map(hdr, _toneMap, token)
                : frames.Sdr, token);
        }

        private static byte[] Crop(byte[] pixels, int width, int height, PixelRect bounds, PixelRect region,
            CancellationToken token)
        {
            if (width <= 0 || height <= 0 || pixels.LongLength != (long)width * height * 4 ||
                bounds.IsEmpty || region.IsEmpty || region.Intersect(bounds) != region)
                throw new CaptureSourceLostException();
            token.ThrowIfCancellationRequested();
            if (width == bounds.Width && height == bounds.Height)
            {
                var output = new byte[checked(region.Width * region.Height * 4)];
                for (var y = 0; y < region.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    Buffer.BlockCopy(pixels, ((region.Y - bounds.Y + y) * width + region.X - bounds.X) * 4,
                        output, y * region.Width * 4, region.Width * 4);
                }

                return output;
            }

            using var image = new MagickImage(pixels,
                new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.BGRA));
            var left = (int)Math.Floor((double)(region.X - bounds.X) * width / bounds.Width);
            var top = (int)Math.Floor((double)(region.Y - bounds.Y) * height / bounds.Height);
            var right = (int)Math.Ceiling((double)(region.Right - bounds.X) * width / bounds.Width);
            var bottom = (int)Math.Ceiling((double)(region.Bottom - bounds.Y) * height / bounds.Height);
            image.Crop(new MagickGeometry(left, top, (uint)(right - left), (uint)(bottom - top)));
            image.ResetPage();
            image.Resize(new MagickGeometry((uint)region.Width, (uint)region.Height) { IgnoreAspectRatio = true });
            token.ThrowIfCancellationRequested();
            using var resized = image.GetPixels();
            return resized.ToByteArray(PixelMapping.BGRA)!;
        }
    }
}

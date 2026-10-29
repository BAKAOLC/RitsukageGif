using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed class FollowingCapture : IFrameSource, IColorFrameSource
    {
        private readonly DesktopCapture _capture;
        private readonly int _frameLength;
        private readonly Func<PixelRect?> _region;
        private PixelRect _current;
        private bool _disposed;
        private CapturedFrameSet? _lastColorLayout;

        public FollowingCapture(RecordingOptions options, Func<DisplayTopology> displayProvider,
            Func<PixelRect?> region)
            : this(options, new DesktopCapture(options, displayProvider), region)
        {
        }

        private FollowingCapture(RecordingOptions options, DesktopCapture capture, Func<PixelRect?> region)
        {
            _region = region;
            _current = options.Region;
            _capture = capture;
            _frameLength = checked(options.Width * options.Height * 4);
        }

        public CapturedFrameSet CaptureFrames(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            var resolved = _region();
            token.ThrowIfCancellationRequested();
            if (resolved is not { IsEmpty: false } region) return BlankColorFrame();
            if (region != _current)
            {
                _capture.Move(region);
                _current = region;
            }

            try
            {
                var frames = _capture.CaptureFrames(token);
                _lastColorLayout = frames with
                {
                    Sdr = frames.Sdr with { Pixels = ReadOnlyMemory<byte>.Empty },
                    Hdr = frames.Hdr is { } hdr ? hdr with { Pixels = ReadOnlyMemory<byte>.Empty } : null,
                };
                return frames;
            }
            catch (CaptureSourceLostException)
            {
                token.ThrowIfCancellationRequested();
                return BlankColorFrame();
            }
        }

        public byte[] Capture()
        {
            return Capture(CancellationToken.None);
        }

        public byte[] Capture(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            var resolved = _region();
            token.ThrowIfCancellationRequested();
            if (resolved is not { IsEmpty: false } region) return new byte[_frameLength];
            if (region != _current)
            {
                _capture.Move(region);
                _current = region;
            }

            try
            {
                return _capture.Capture(token);
            }
            catch (CaptureSourceLostException)
            {
                token.ThrowIfCancellationRequested();
                return new byte[_frameLength];
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _capture.Dispose();
        }

        private CapturedFrameSet BlankColorFrame()
        {
            if (_lastColorLayout is not { } layout) throw new CaptureSourceLostException();
            return new(Blank(layout.Sdr), layout.Hdr is { } hdr ? Blank(hdr) : null,
                Stopwatch.GetTimestamp());

            static CapturedFrame Blank(CapturedFrame frame)
            {
                var pixels = new byte[checked(frame.Stride * frame.Height)];
                var componentBytes = frame.PixelFormat == CapturePixelFormat.Rgba16FloatScRgb ? 2 : 1;
                for (var y = 0; y < frame.Height; y++)
                for (var x = 0; x < frame.Width; x++)
                {
                    var offset = y * frame.Stride + (x * 4 + 3) * componentBytes;
                    if (componentBytes == 1) pixels[offset] = 255;
                    else pixels[offset + 1] = 0x3c;
                }

                return frame with { Pixels = pixels, AlphaMode = CaptureAlphaMode.Opaque, Revision = -1 };
            }
        }
    }
}

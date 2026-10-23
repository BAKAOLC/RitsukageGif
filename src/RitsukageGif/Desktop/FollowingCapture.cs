namespace RitsukageGif.Desktop
{
    internal sealed class FollowingCapture : IFrameSource
    {
        private readonly Func<PixelRect?> _region;
        private readonly DesktopCapture _capture;
        private readonly int _frameLength;
        private PixelRect _current;
        private bool _disposed;

        public FollowingCapture(RecordingOptions options, PixelRect[] monitors, Func<PixelRect?> region)
            : this(options, new DesktopCapture(options, monitors), region)
        {
        }

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

        public byte[] Capture() => Capture(default);

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
    }
}

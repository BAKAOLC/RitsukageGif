namespace RitsukageGif.Desktop
{
    internal sealed class RecordingAnnotationWorker : IDisposable
    {
        private readonly ScreenshotAnnotationSession _pixels = new() { CanScroll = false };
        private bool _disposed;
        private byte[]? _sdrOriginal;
        private byte[]? _sdrWorking;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sdrOriginal = _sdrWorking = null;
            _pixels.Dispose();
        }

        internal Task<CapturedFrame> ComposeAsync(RecordingAnnotationSnapshot? snapshot, CapturedFrame source,
            CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            if (snapshot is null || snapshot.Layers.Count == 0) return Task.FromResult(source);
            if (source.Width != snapshot.NativeSize.Width || source.Height != snapshot.NativeSize.Height)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(source));
            if (source.PixelFormat == CapturePixelFormat.Bgra8Srgb)
            {
                var length = checked(source.Width * source.Height * 8);
                if (_sdrWorking?.Length != length) _sdrWorking = new byte[length];
                if (snapshot.NeedsOriginalSamples && _sdrOriginal?.Length != length)
                    _sdrOriginal = new byte[length];
            }

            return _pixels.ComposeRecordingSnapshotAsync(snapshot, source, _sdrWorking,
                snapshot.NeedsOriginalSamples ? _sdrOriginal : null, token);
        }
    }
}

namespace RitsukageGif.Desktop
{
    internal sealed class NativeCaptureSelection(NativeCaptureSession session, long version, CaptureSourceFrame frame)
        : IDisposable
    {
        private readonly Lock _gate = new();
        private NativeCaptureSession? _session = session;

        public CaptureSourceFrame Frame { get; } = frame;

        public void Dispose()
        {
            NativeCaptureSession? candidate;
            lock (_gate)
            {
                candidate = _session;
                _session = null;
            }

            candidate?.Dispose();
        }

        public void Commit(CancellationToken token = default)
        {
            NativeCaptureSession? previous;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_session is null, this);
                previous = NativeCaptureSession.CommitSelection(_session, version, token);
                _session = null;
            }

            previous?.Dispose();
        }
    }
}

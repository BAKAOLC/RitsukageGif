namespace RitsukageGif.Desktop
{
    internal sealed class NativeCaptureSelection(
        NativeCaptureSession session,
        long version,
        NativeCaptureSnapshot snapshot)
        : IDisposable
    {
        private readonly Lock _gate = new();
        private NativeCaptureSession? _session = session;

        public CaptureSourceFrame Frame { get; } = new(snapshot.Frames.Sdr.Width, snapshot.Frames.Sdr.Height,
                CapturedFramePixels.ToStraightBgra(snapshot.Frames.Sdr), snapshot.Geometry.X, snapshot.Geometry.Y)
            { CoordinateBounds = snapshot.Geometry.Bounds, PixelDensity = snapshot.Geometry.BackingScale };

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

        internal NativeCaptureSnapshot ReadSnapshot(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_session is null, this);
                return snapshot;
            }
        }

        internal NativeCaptureSessionReference RetainSource()
        {
            lock (_gate)
                return (_session ?? throw new CaptureSourceLostException()).Retain();
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

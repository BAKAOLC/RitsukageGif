namespace RitsukageGif.Desktop
{
    internal sealed class NativeCaptureSessionReference(NativeCaptureSession session) : IDisposable
    {
        private int _disposed;

        internal NativeCaptureSession Session { get; } = session;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Session.ReleaseReference();
        }
    }
}

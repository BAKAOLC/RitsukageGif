namespace RitsukageGif.Desktop
{
    internal sealed class NativeCaptureDemand(NativeCaptureSessionReference reference) : IDisposable
    {
        private long _id;
        private bool _registered;
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Suspend();
            reference.Dispose();
        }

        internal void Suspend()
        {
            if (!_registered) return;
            reference.Session.RemoveDemand(_id);
            _registered = false;
        }

        internal void Use(Action<NativeCaptureSnapshot> consume, PixelRect? region = null, double? density = null,
            bool requireHdr = false, int? fps = null, bool? retainHdr = null, CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var source = reference.Session;
            if (!_registered)
            {
                _id = source.RegisterDemand();
                _registered = true;
            }

            source.ConfigureDemand(_id, region, density, token, fps, retainHdr ?? requireHdr);
            source.UseSnapshot(consume, requireHdr, token);
        }
    }
}

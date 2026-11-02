namespace RitsukageGif.Desktop
{
    internal sealed class NativeCaptureDemand(Func<NativeCaptureSession> resolve) : IDisposable
    {
        private NativeCaptureSession? _session;
        private long _id;

        public void Dispose()
        {
            _session?.RemoveDemand(_id);
            _session = null;
        }

        internal void Use(Action<NativeCaptureSnapshot> consume, PixelRect? region = null, double? density = null,
            bool requireHdr = false, CancellationToken token = default)
        {
            var current = resolve();
            if (!ReferenceEquals(current, _session))
            {
                _session?.RemoveDemand(_id);
                _session = current;
                _id = current.RegisterDemand();
            }

            current.ConfigureDemand(_id, region, density, token);
            current.UseSnapshot(consume, requireHdr, token);
        }
    }
}

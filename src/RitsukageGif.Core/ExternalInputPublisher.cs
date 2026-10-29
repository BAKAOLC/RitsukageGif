namespace RitsukageGif.Core
{
    public sealed class ExternalInputPublisher : IDisposable
    {
        private readonly ExternalInputHub _hub;
        private readonly Guid _lease;
        private readonly string _name;
        private int _disposed;

        internal ExternalInputPublisher(ExternalInputHub hub, string name, Guid lease)
        {
            _hub = hub;
            _name = name;
            _lease = lease;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) _hub.Disconnect(_name, _lease);
        }

        public bool TryPublishFrame(int width, int height, ReadOnlySpan<byte> pixels, double? timestamp = null)
        {
            return Volatile.Read(ref _disposed) == 0 && _hub.PublishFrame(_name, _lease, width, height, pixels,
                timestamp ?? ExternalInputHub.Now);
        }

        public bool TryPublishJson(string json, double? timestamp = null)
        {
            return Volatile.Read(ref _disposed) == 0 &&
                   _hub.PublishJson(_name, _lease, json, timestamp ?? ExternalInputHub.Now);
        }
    }
}

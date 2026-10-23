using System.Diagnostics;

namespace RitsukageGif.Core
{
    public sealed partial class ExternalInputHub : IInputChannelReader, IDisposable
    {
        public const int MaximumChannels = 16;
        public const int MaximumFramesPerChannel = 120;
        public const long MaximumChannelBytes = 64L * 1024 * 1024;
        public const long MaximumTotalBytes = 256L * 1024 * 1024;
        public const int MaximumJsonBytes = 1024 * 1024;
        private readonly object _gate = new();
        private readonly Dictionary<string, ChannelState> _channels = new(StringComparer.Ordinal);
        private long _bytes;
        private long _revision;
        private bool _disposed;
        private TaskCompletionSource<bool> _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        public long BufferedBytes
        {
            get
            {
                lock (_gate) return _bytes;
            }
        }

        public IReadOnlyList<string> Names
        {
            get
            {
                lock (_gate) return _channels.Keys.Order(StringComparer.Ordinal).ToArray();
            }
        }

        public ExternalInputPublisher OpenPublisher(string name)
        {
            ValidateName(name);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_channels.TryGetValue(name, out var current) && current.Connected)
                    throw new InvalidOperationException(name);
                if (current is not null) Remove(current);
                else if (_channels.Count >= MaximumChannels)
                {
                    var oldest = _channels.Values.Where(channel => !channel.Connected)
                        .MinBy(channel => channel.DataVersion);
                    if (oldest is null) throw new InvalidOperationException(UiText.Get("MemoryLimit"));
                    Remove(oldest);
                }

                var channel = new ChannelState(name, Guid.NewGuid(), ++_revision, Now);
                _channels.Add(name, channel);
                Signal();
                return new ExternalInputPublisher(this, name, channel.Lease);
            }
        }

        public InputChannelData? Read(string name)
        {
            lock (_gate)
                return _channels.TryGetValue(name, out var channel)
                    ? new InputChannelData(channel.Json, channel.DataVersion, channel.DataTimestamp, channel.Connected)
                    : null;
        }

        public ExternalInputFrame? ReadFrame(string name, double targetTimestamp, double staleAfter = 1,
            bool holdLast = false)
        {
            if (!double.IsFinite(targetTimestamp) || targetTimestamp < 0)
                throw new ArgumentOutOfRangeException(nameof(targetTimestamp));
            if (double.IsNaN(staleAfter) || staleAfter < 0) throw new ArgumentOutOfRangeException(nameof(staleAfter));
            lock (_gate)
            {
                if (!_channels.TryGetValue(name, out var channel) || (!channel.Connected && !holdLast)) return null;
                for (var node = channel.Frames.Last; node is not null; node = node.Previous)
                {
                    var frame = node.Value;
                    if (frame.Timestamp > targetTimestamp) continue;
                    return holdLast || targetTimestamp - frame.Timestamp <= staleAfter ? frame : null;
                }

                return null;
            }
        }

        public ExternalInputChannelStatus? GetStatus(string name)
        {
            lock (_gate)
                return _channels.TryGetValue(name, out var channel)
                    ? new ExternalInputChannelStatus(channel.Name, channel.Connected, channel.Frames.Count,
                        channel.FrameBytes + channel.JsonBytes, channel.Published, channel.Dropped,
                        channel.FrameVersion, channel.DataVersion,
                        double.IsNegativeInfinity(channel.FrameTimestamp) ? null : channel.FrameTimestamp,
                        channel.Json is null ? null : channel.DataTimestamp)
                    : null;
        }

        public bool RemoveChannel(string name)
        {
            lock (_gate)
            {
                if (!_channels.TryGetValue(name, out var channel)) return false;
                Remove(channel);
                return true;
            }
        }

        internal void Disconnect(string name, Guid lease)
        {
            lock (_gate)
            {
                if (!TryLease(name, lease, out var channel)) return;
                channel.Connected = false;
                channel.DataVersion = ++_revision;
                Signal();
            }
        }

        private static void ValidateName(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (name.Length > 128 || name.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-' or '.')))
                throw new ArgumentException(nameof(name));
        }

        private bool TryLease(string name, Guid lease, out ChannelState channel)
        {
            return _channels.TryGetValue(name, out channel!) && channel.Connected && channel.Lease == lease &&
                   !_disposed;
        }

        private void Remove(ChannelState channel)
        {
            _bytes -= channel.FrameBytes + channel.JsonBytes;
            _channels.Remove(channel.Name);
            Signal();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _channels.Clear();
                _bytes = 0;
                Signal();
            }
        }

        private sealed class ChannelState(string name, Guid lease, long version, double timestamp)
        {
            public readonly string Name = name;
            public readonly Guid Lease = lease;
            public readonly LinkedList<ExternalInputFrame> Frames = new();
            public bool Connected = true;
            public long DataVersion = version;
            public double DataTimestamp = timestamp;
            public double JsonTimestamp = double.NegativeInfinity;
            public double FrameTimestamp = double.NegativeInfinity;
            public long FrameVersion;
            public string? Json;
            public long JsonBytes;
            public long FrameBytes;
            public long Published;
            public long Dropped;
        }
    }
}

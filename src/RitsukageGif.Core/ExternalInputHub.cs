using System.Diagnostics;

namespace RitsukageGif.Core
{
    public sealed partial class ExternalInputHub : IInputChannelReader, IDisposable
    {
        // ReSharper disable once MemberCanBePrivate.Global
        public const int MaximumChannels = 16;

        // ReSharper disable once MemberCanBePrivate.Global
        public const int MaximumFramesPerChannel = 120;

        // ReSharper disable once MemberCanBePrivate.Global
        public const long MaximumChannelBytes = 64L * 1024 * 1024;

        // ReSharper disable once MemberCanBePrivate.Global
        public const long MaximumTotalBytes = 256L * 1024 * 1024;

        // ReSharper disable once MemberCanBePrivate.Global
        public const int MaximumJsonBytes = 1024 * 1024;
        private static readonly ILogger Logger = LogHub.GetLogger("Core.MediaInput");
        private readonly Dictionary<string, ChannelState> _channels = new(StringComparer.Ordinal);
        private readonly Lock _gate = new();
        private long _bytes;
        private TaskCompletionSource<bool> _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _disposed;
        private long _revision;

        public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        // ReSharper disable once UnusedMember.Global
        public long BufferedBytes
        {
            get
            {
                lock (_gate) return _bytes;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                Logger.ZLogDebug($"External input hub disposed: channels={_channels.Count}, bufferedBytes={_bytes}");
                _channels.Clear();
                _bytes = 0;
                Signal();
            }
        }

        public IReadOnlyList<string> Names
        {
            get
            {
                lock (_gate) return [.. _channels.Keys.Order(StringComparer.Ordinal)];
            }
        }

        public InputChannelData? Read(string name)
        {
            lock (_gate)
                return _channels.TryGetValue(name, out var channel)
                    ? new InputChannelData(channel.Json, channel.DataVersion, channel.DataTimestamp, channel.Connected)
                    : null;
        }

        public ExternalInputPublisher OpenPublisher(string name)
        {
            ValidateName(name);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_channels.TryGetValue(name, out var current) && current.Connected)
                    throw new InvalidOperationException(name);
                if (current is not null)
                {
                    Remove(current);
                }
                else if (_channels.Count >= MaximumChannels)
                {
                    var oldest = _channels.Values.Where(channel => !channel.Connected)
                        .MinBy(channel => channel.DataVersion);
                    if (oldest is null) throw new InvalidOperationException(UiText.Get("MemoryLimit"));
                    Remove(oldest);
                }

                var channel = new ChannelState(name, Guid.NewGuid(), ++_revision, Now);
                _channels.Add(name, channel);
                Logger.ZLogInformation(
                    $"External input publisher connected: lease={channel.Lease}, channels={_channels.Count}");
                Signal();
                return new(this, name, channel.Lease);
            }
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

        // ReSharper disable once UnusedMethodReturnValue.Global
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
                Logger.ZLogInformation(
                    $"External input publisher disconnected: lease={channel.Lease}, publishedFrames={channel.Published}, droppedFrames={channel.Dropped}, bufferedFrames={channel.Frames.Count}");
                channel.DataVersion = ++_revision;
                Signal();
            }
        }

        private static void ValidateName(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (name.Length > 128 || name.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-' or '.')))
                throw new ArgumentException(null, nameof(name));
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

        private sealed class ChannelState(string name, Guid lease, long version, double timestamp)
        {
            public readonly LinkedList<ExternalInputFrame> Frames = new();
            public readonly Guid Lease = lease;
            public readonly string Name = name;
            public bool Connected = true;
            public double DataTimestamp = timestamp;
            public long DataVersion = version;
            public long Dropped;
            public long FrameBytes;
            public double FrameTimestamp = double.NegativeInfinity;
            public long FrameVersion;
            public string? Json;
            public long JsonBytes;
            public double JsonTimestamp = double.NegativeInfinity;
            public long Published;
        }
    }
}

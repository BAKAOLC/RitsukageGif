using System.Text;
using System.Text.Json;

namespace RitsukageGif.Core
{
    public sealed partial class ExternalInputHub
    {
        private static readonly UTF8Encoding Utf8 = new(false, true);

        internal bool PublishFrame(string name, Guid lease, int width, int height, ReadOnlySpan<byte> pixels,
            double timestamp)
        {
            if (width <= 0 || height <= 0 || (long)width * height * 4 != pixels.Length ||
                pixels.Length > MaximumChannelBytes)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"));
            ValidateTimestamp(timestamp);
            lock (_gate)
            {
                if (!TryLease(name, lease, out var channel)) return false;
                if (timestamp <= channel.FrameTimestamp || pixels.Length + channel.JsonBytes > MaximumChannelBytes)
                {
                    channel.Dropped++;
                    return false;
                }

                while (channel.Frames.Count >= MaximumFramesPerChannel ||
                       channel.FrameBytes + channel.JsonBytes + pixels.Length > MaximumChannelBytes)
                    EvictFrame(channel);
                EnsureCapacity(pixels.Length);
                // The publisher may reuse its receive buffer immediately after this copy. Readers retain
                // snapshots, so evicted arrays must not be pooled or overwritten by subsequent frames.
                var owned = pixels.ToArray();
                var frame = new ExternalInputFrame(width, height, owned, timestamp, ++_revision);
                channel.Frames.AddLast(frame);
                channel.FrameBytes += owned.Length;
                _bytes += owned.Length;
                channel.FrameTimestamp = timestamp;
                channel.FrameVersion = frame.Version;
                if (channel.Published++ == 0)
                    Logger.ZLogInformation(
                        $"External input publisher produced its first frame: lease={channel.Lease}, width={width}, height={height}, bytes={owned.Length}");
                return true;
            }
        }

        internal bool PublishJson(string name, Guid lease, string json, double timestamp)
        {
            ArgumentNullException.ThrowIfNull(json);
            ValidateTimestamp(timestamp);
            if (json.Length > MaximumJsonBytes || Utf8.GetByteCount(json) > MaximumJsonBytes)
                throw new ArgumentException(null, nameof(json));
            try
            {
                using var document = JsonDocument.Parse(json, new() { MaxDepth = 64 });
                var remaining = 100000;
                ValidateJson(document.RootElement, ref remaining);
            }
            catch (JsonException ex)
            {
                throw new ArgumentException(null, nameof(json), ex);
            }

            lock (_gate)
            {
                if (!TryLease(name, lease, out var channel) || timestamp <= channel.JsonTimestamp) return false;
                var bytes = json.Length * 2L;
                while (channel.FrameBytes + bytes > MaximumChannelBytes) EvictFrame(channel);
                EnsureCapacity(Math.Max(0, bytes - channel.JsonBytes));
                _bytes += bytes - channel.JsonBytes;
                channel.JsonBytes = bytes;
                channel.Json = json;
                channel.JsonTimestamp = timestamp;
                channel.DataTimestamp = timestamp;
                channel.DataVersion = ++_revision;
                Signal();
                return true;
            }
        }

        private static void ValidateJson(JsonElement element, ref int remaining)
        {
            if (--remaining < 0) throw new ArgumentException(UiText.Get("ServiceLimitExceeded"));
            switch (element.ValueKind)
            {
                case JsonValueKind.Number:
                    if (!element.TryGetDouble(out var number) || !double.IsFinite(number))
                        throw new ArgumentException(UiText.Get("ScriptTypeError"));
                    break;
                case JsonValueKind.String:
                    _ = element.GetString();
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray()) ValidateJson(item, ref remaining);
                    break;
                case JsonValueKind.Object:
                    var keys = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in element.EnumerateObject())
                    {
                        if (!keys.Add(property.Name)) throw new ArgumentException(UiText.Get("ScriptTypeError"));
                        ValidateJson(property.Value, ref remaining);
                    }

                    break;
                case JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False:
                    break;
                default:
                    throw new ArgumentException(UiText.Get("ScriptTypeError"));
            }
        }

        private static void ValidateTimestamp(double timestamp)
        {
            if (!double.IsFinite(timestamp) || timestamp < 0) throw new ArgumentOutOfRangeException(nameof(timestamp));
        }

        private void EnsureCapacity(long additional)
        {
            while (_bytes + additional > MaximumTotalBytes)
            {
                ChannelState? oldest = null;
                foreach (var channel in _channels.Values)
                    if (channel.Frames.First is { } first &&
                        (oldest is null || first.Value.Timestamp < oldest.Frames.First!.Value.Timestamp))
                        oldest = channel;
                // At most 16 one-MiB JSON values remain when all frame queues are empty.
                if (oldest is null) throw new InvalidOperationException(UiText.Get("MemoryLimit"));
                EvictFrame(oldest);
            }
        }

        private void EvictFrame(ChannelState channel)
        {
            var frame = channel.Frames.First!.Value;
            channel.Frames.RemoveFirst();
            channel.FrameBytes -= frame.Pixels.Length;
            _bytes -= frame.Pixels.Length;
            channel.Dropped++;
        }
    }
}

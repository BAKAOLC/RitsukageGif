using System.Text;
using Avalonia.Input;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed class RecordingInputTraceDocument : IDisposable
    {
        private readonly Lock _sync = new();
        private readonly BinaryReader _frames, _events, _shapes, _geometry;
        private readonly long _frameCount, _eventCount;
        private readonly Dictionary<long, RecordingCursorSample> _shapeCache = new();
        private long _cachedGeometryOffset = -1;
        private IReadOnlyList<RecordingInputGeometry> _cachedGeometry = [];
        private long _cachedShapeBytes;

        private RecordingInputTraceDocument(string path)
        {
            _frames = Open(path);
            try
            {
                if (_frames.ReadInt32() != RecordingInputTraceWriter.Magic ||
                    _frames.ReadInt32() != RecordingInputTraceWriter.FrameSize) throw Invalid();
                TimestampFrequency = _frames.ReadInt64();
                var flags = _frames.ReadInt32();
                HasKeyboard = (flags & 1) != 0;
                HasMouse = (flags & 2) != 0;
                HasCursor = (flags & 4) != 0;
                UnavailableReason = File.Exists(path + ".reason") ? File.ReadAllText(path + ".reason") : null;
                if (TimestampFrequency <= 0 || _frames.BaseStream.Length < RecordingInputTraceWriter.HeaderSize ||
                    (_frames.BaseStream.Length - RecordingInputTraceWriter.HeaderSize) %
                    RecordingInputTraceWriter.FrameSize != 0) throw Invalid();
                _frameCount = (_frames.BaseStream.Length - RecordingInputTraceWriter.HeaderSize) /
                              RecordingInputTraceWriter.FrameSize;
                _events = Open(path + ".events");
                _shapes = Open(path + ".shapes");
                _geometry = Open(path + ".geometry");
                if (_events.BaseStream.Length % RecordingInputTraceWriter.EventSize != 0) throw Invalid();
                _eventCount = _events.BaseStream.Length / RecordingInputTraceWriter.EventSize;
            }
            catch
            {
                _frames.Dispose();
                _events?.Dispose();
                _shapes?.Dispose();
                _geometry?.Dispose();
                throw;
            }
        }

        internal bool HasKeyboard { get; }
        internal string? UnavailableReason { get; }
        internal bool HasMouse { get; }
        internal bool HasCursor { get; }
        private long TimestampFrequency { get; }

        public void Dispose()
        {
            lock (_sync)
            {
                _frames.Dispose();
                _events.Dispose();
                _shapes.Dispose();
                _geometry.Dispose();
                _shapeCache.Clear();
            }
        }

        internal static RecordingInputTraceDocument? TryOpenForMedia(string mediaPath)
        {
            var path = mediaPath + ".rgtrace";
            return File.Exists(path) ? new(path) : null;
        }

        internal RecordingInputTraceFrame? FrameAt(TimeSpan position)
        {
            lock (_sync)
            {
                if (_frameCount == 0) return null;
                var index = Math.Max(0, LowerBound(_frames, _frameCount, RecordingInputTraceWriter.HeaderSize,
                    RecordingInputTraceWriter.FrameSize, 0, position.Ticks + 1) - 1);
                return ReadFrame(index);
            }
        }

        internal RecordingInputTraceFrame? FrameAfter(TimeSpan position)
        {
            lock (_sync)
            {
                var index = LowerBound(_frames, _frameCount, RecordingInputTraceWriter.HeaderSize,
                    RecordingInputTraceWriter.FrameSize, 0, position.Ticks + 1);
                return index < _frameCount ? ReadFrame(index) : null;
            }
        }

        private RecordingInputTraceFrame ReadFrame(long index)
        {
            _frames.BaseStream.Position = FrameOffset(index);
            var pts = TimeSpan.FromTicks(_frames.ReadInt64());
            var timestamp = _frames.ReadInt64();
            var geometry = Geometry(_frames.ReadInt64());
            var shapeOffset = _frames.ReadInt64();
            var x = _frames.ReadDouble();
            var y = _frames.ReadDouble();
            var scale = _frames.ReadDouble();
            var visible = _frames.ReadInt32() != 0;
            var width = _frames.ReadInt32();
            var height = _frames.ReadInt32();
            var buttons = _frames.ReadInt32();
            var sequence = _frames.ReadInt64();
            var keyboardStart = _frames.ReadInt64();
            var mouseStart = _frames.ReadInt64();
            var cursor = (shapeOffset < 0 ? RecordingCursorSample.Hidden : Shape(shapeOffset)) with
            {
                X = x, Y = y, PixelScale = scale, Visible = visible, Buttons = buttons,
            };
            if (width <= 0 || height <= 0 || !double.IsFinite(scale) || scale <= 0 ||
                !double.IsFinite(x) || !double.IsFinite(y) || keyboardStart < -1 || mouseStart < -1) throw Invalid();
            return new(pts, timestamp, geometry, cursor, width, height, sequence, keyboardStart, mouseStart);
        }

        // ReSharper disable once UnusedMember.Global
        internal IEnumerable<TimeSpan> GetFramePositions(TimeSpan start, TimeSpan end)
        {
            long index;
            lock (_sync)
                index = LowerBound(_frames, _frameCount, RecordingInputTraceWriter.HeaderSize,
                    RecordingInputTraceWriter.FrameSize, 0, start.Ticks);
            for (; index < _frameCount; index++)
            {
                long ticks;
                lock (_sync) ticks = ReadFrameTime(index, 0);
                if (ticks > end.Ticks) yield break;
                yield return TimeSpan.FromTicks(ticks);
            }
        }

        internal IEnumerable<(TimeSpan Position, RecordingInputEvent Input)> EventsBefore(TimeSpan position,
            TimeSpan duration)
        {
            long index;
            lock (_sync)
                index = LowerBound(_events, _eventCount, 0, RecordingInputTraceWriter.EventSize, 0,
                    position.Ticks + 1) - 1;
            for (; index >= 0; index--)
            {
                RecordingInputEvent input;
                TimeSpan pts;
                lock (_sync)
                {
                    _events.BaseStream.Position = index * RecordingInputTraceWriter.EventSize;
                    pts = TimeSpan.FromTicks(_events.ReadInt64());
                    if (position - pts > duration) yield break;
                    var timestamp = _events.ReadInt64();
                    input = new(timestamp, (RecordingInputEventKind)_events.ReadInt32(), (Key)_events.ReadInt32(),
                        (KeyModifiers)_events.ReadInt32(), _events.ReadInt32(), _events.ReadDouble(),
                        _events.ReadDouble(), _events.ReadInt32(), _events.ReadUInt32(), _events.ReadUInt32());
                }

                yield return (pts, input);
            }
        }

        private IReadOnlyList<RecordingInputGeometry> Geometry(long offset)
        {
            if (_cachedGeometryOffset == offset) return _cachedGeometry;
            _geometry.BaseStream.Position = offset;
            var count = _geometry.ReadInt32();
            if (count is < 0 or > 65536) throw Invalid();
            var layers = new RecordingInputGeometry[count];
            for (var i = 0; i < count; i++)
            {
                var region = ReadRect(_geometry);
                var destination = new SKRect(_geometry.ReadSingle(), _geometry.ReadSingle(),
                    _geometry.ReadSingle(), _geometry.ReadSingle());
                var owns = _geometry.ReadBoolean();
                var evenOdd = _geometry.ReadBoolean();
                var bytes = _geometry.Read7BitEncodedInt();
                if (bytes is < 0 or > 1024 * 1024) throw Invalid();
                var pathBytes = _geometry.ReadBytes(bytes);
                if (pathBytes.Length != bytes) throw Invalid();
                var path = Encoding.UTF8.GetString(pathBytes);
                layers[i] = new(region, destination, owns, path.Length == 0 ? null : path, evenOdd);
            }

            _cachedGeometryOffset = offset;
            return _cachedGeometry = layers;
        }

        private RecordingCursorSample Shape(long offset)
        {
            if (_shapeCache.TryGetValue(offset, out var cached)) return cached;
            _shapes.BaseStream.Position = offset;
            var width = _shapes.ReadInt32();
            var height = _shapes.ReadInt32();
            var hotX = _shapes.ReadInt32();
            var hotY = _shapes.ReadInt32();
            var hasMask = _shapes.ReadBoolean();
            if (width is <= 0 or > 4096 || height is <= 0 or > 4096) throw Invalid();
            var length = checked(width * height * 4);
            var pixels = _shapes.ReadBytes(length);
            if (pixels.Length != length) throw Invalid();
            var mask = hasMask ? _shapes.ReadBytes(length) : null;
            if (mask is not null && mask.Length != length) throw Invalid();
            var shape = new RecordingCursorSample((ulong)offset, true, 0, 0, width, height, hotX, hotY, 1, pixels,
                mask);
            if (_cachedShapeBytes + length * (hasMask ? 2 : 1) > 8 * 1024 * 1024)
            {
                _shapeCache.Clear();
                _cachedShapeBytes = 0;
            }

            if (length * (hasMask ? 2 : 1) <= 8 * 1024 * 1024)
            {
                _shapeCache.Add(offset, shape);
                _cachedShapeBytes += length * (hasMask ? 2 : 1);
            }

            return shape;
        }

        private long ReadFrameTime(long index, int field)
        {
            _frames.BaseStream.Position = FrameOffset(index) + field;
            return _frames.ReadInt64();
        }

        private static long FrameOffset(long index)
        {
            return RecordingInputTraceWriter.HeaderSize + index * RecordingInputTraceWriter.FrameSize;
        }

        private static long LowerBound(BinaryReader reader, long count, int header, int stride, int field, long value)
        {
            var low = 0L;
            var high = count;
            while (low < high)
            {
                var mid = low + (high - low) / 2;
                reader.BaseStream.Position = header + mid * stride + field;
                if (reader.ReadInt64() < value) low = mid + 1;
                else high = mid;
            }

            return low;
        }

        private static PixelRect ReadRect(BinaryReader reader)
        {
            return new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        }

        private static BinaryReader Open(string path)
        {
            return new(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                FileOptions.RandomAccess), Encoding.UTF8);
        }

        private static InvalidDataException Invalid()
        {
            return new("Invalid recording input trace.");
        }
    }
}

using System.Diagnostics;
using System.Text;

namespace RitsukageGif.Desktop
{
    internal sealed class RecordingInputTraceWriter : IDisposable
    {
        internal const int HeaderSize = 32;
        internal const int FrameSize = 96;
        internal const int EventSize = 64;
        private readonly BinaryWriter _frames, _events, _shapes, _geometry;
        private readonly string _path;
        private readonly Lock _eventLock = new();
        private readonly Lock _frameLock = new();
        private readonly Dictionary<ulong, long> _shapeOffsets = new();
        private IReadOnlyList<RecordingInputGeometry>? _lastGeometry;
        private long _geometryOffset, _lastEventTimestamp, _lastEventPosition;
        private int _trackFlags;

        internal RecordingInputTraceWriter(string mediaPath, bool keyboard, bool mouse)
        {
            var path = mediaPath + ".rgtrace";
            _path = path;
            _frames = Open(path);
            try
            {
                _events = Open(path + ".events");
                _shapes = Open(path + ".shapes");
                _geometry = Open(path + ".geometry");
                _frames.Write(0x31544752);
                _frames.Write(FrameSize);
                _frames.Write(Stopwatch.Frequency);
                _trackFlags = (keyboard ? 1 : 0) | (mouse ? 2 : 0) | 4;
                _frames.Write(_trackFlags);
                _frames.Write(0);
                _frames.Write(0L);
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

        public void Dispose()
        {
            _frames.Dispose();
            lock (_eventLock) _events.Dispose();
            _shapes.Dispose();
            _geometry.Dispose();
        }

        internal void SetUnavailableTracks(string reason)
        {
            _trackFlags &= 4;
            WriteCapabilities(reason);
        }

        internal void SetCursorUnavailable(string reason)
        {
            _trackFlags &= ~4;
            WriteCapabilities(reason);
        }

        private void WriteCapabilities(string reason)
        {
            lock (_frameLock)
            {
                var position = _frames.BaseStream.Position;
                _frames.BaseStream.Position = 16;
                _frames.Write(_trackFlags);
                _frames.BaseStream.Position = position;
                var path = _path + ".reason";
                var existing = File.Exists(path) ? File.ReadAllText(path) + Environment.NewLine : string.Empty;
                File.WriteAllText(path, existing + reason);
            }
        }

        internal void AppendFrame(RecordingFrameObservation frame, IReadOnlyList<RecordingInputGeometry> geometry,
            RecordingCursorSample cursor, long keyboardStartTimestamp = 0, long mouseStartTimestamp = 0)
        {
            lock (_frameLock)
            {
                if (_lastGeometry is null || !geometry.SequenceEqual(_lastGeometry))
                {
                    _geometryOffset = _geometry.BaseStream.Position;
                    _geometry.Write(geometry.Count);
                    foreach (var layer in geometry)
                    {
                        WriteRect(_geometry, layer.Region);
                        WriteRect(_geometry, layer.Destination);
                        _geometry.Write(layer.OwnsCursor);
                        _geometry.Write(layer.ClipEvenOdd);
                        _geometry.Write(layer.ClipPathData ?? string.Empty);
                    }

                    _lastGeometry = [.. geometry];
                }

                var shapeOffset = -1L;
                if (cursor.Visible && cursor.Pixels.Length != 0)
                    if (!_shapeOffsets.TryGetValue(cursor.ShapeId, out shapeOffset))
                    {
                        shapeOffset = _shapes.BaseStream.Position;
                        var length = checked(cursor.Width * cursor.Height * 4);
                        if (cursor.Width is <= 0 or > 4096 || cursor.Height is <= 0 or > 4096 ||
                            cursor.Pixels.Length != length ||
                            (cursor.AndMask is { } mask && mask.Length != length))
                            throw new InvalidDataException("Invalid cursor shape.");
                        _shapes.Write(cursor.Width);
                        _shapes.Write(cursor.Height);
                        _shapes.Write(cursor.HotspotX);
                        _shapes.Write(cursor.HotspotY);
                        _shapes.Write(cursor.AndMask is not null);
                        _shapes.Write(cursor.Pixels);
                        if (cursor.AndMask is { } andMask) _shapes.Write(andMask);
                        // Native shape identifiers provide identity without hashing complete cursor images per frame.
                        if (_shapeOffsets.Count == 32) _shapeOffsets.Clear();
                        _shapeOffsets.Add(cursor.ShapeId, shapeOffset);
                    }

                _frames.Write(frame.Position.Ticks);
                _frames.Write(frame.CaptureTimestamp);
                _frames.Write(_geometryOffset);
                _frames.Write(shapeOffset);
                _frames.Write(cursor.X);
                _frames.Write(cursor.Y);
                _frames.Write(cursor.PixelScale);
                _frames.Write(cursor.Visible ? 1 : 0);
                _frames.Write(frame.Width);
                _frames.Write(frame.Height);
                _frames.Write(cursor.Buttons);
                _frames.Write(frame.CaptureSequence);
                _frames.Write(keyboardStartTimestamp);
                _frames.Write(mouseStartTimestamp);
            }
        }

        internal void AppendEvent(RecordingInputEvent input, TimeSpan position)
        {
            lock (_eventLock)
            {
                var timestamp = Math.Max(_lastEventTimestamp, input.Timestamp);
                _lastEventTimestamp = timestamp;
                _lastEventPosition = Math.Max(_lastEventPosition, position.Ticks);
                _events.Write(_lastEventPosition);
                _events.Write(timestamp);
                _events.Write((int)input.Kind);
                _events.Write((int)input.Key);
                _events.Write((int)input.Modifiers);
                _events.Write(input.Button);
                _events.Write(input.X);
                _events.Write(input.Y);
                _events.Write(input.WheelDelta);
                _events.Write(input.NativeCode);
                _events.Write(input.NativeScanCode);
                _events.Write(0);
            }
        }

        private static BinaryWriter Open(string path)
        {
            return new(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536,
                FileOptions.SequentialScan), Encoding.UTF8);
        }

        private static void WriteRect(BinaryWriter writer, PixelRect rect)
        {
            writer.Write(rect.X);
            writer.Write(rect.Y);
            writer.Write(rect.Width);
            writer.Write(rect.Height);
        }
    }
}

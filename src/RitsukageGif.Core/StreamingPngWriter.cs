using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;

namespace RitsukageGif.Core
{
    internal sealed class StreamingPngWriter : IDisposable
    {
        private static readonly uint[] CrcTable = CreateCrcTable();
        private readonly Stream _stream;
        private readonly int _width;
        private readonly CancellationToken _token;
        private readonly byte[] _row;
        private readonly byte[] _lastControl = new byte[26];
        private readonly long _animationPosition;
        private long _controlPosition;
        private uint _sequence, _frames;
        private int _lastDelay;

        public StreamingPngWriter(Stream stream, int width, int height, CancellationToken token)
        {
            _stream = stream;
            _width = width;
            _token = token;
            stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);
            Span<byte> header = stackalloc byte[13];
            header.Clear();
            BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
            BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)height);
            header[8] = 8;
            header[9] = 6;
            WriteChunk("IHDR"u8, header);
            _animationPosition = stream.Position;
            Span<byte> animation = stackalloc byte[8];
            animation.Clear();
            WriteChunk("acTL"u8, animation);
            _row = ArrayPool<byte>.Shared.Rent(checked(width * 4 + 1));
        }

        public void Add(byte[] pixels, PixelRect bounds, int delay)
        {
            if (bounds.IsEmpty)
            {
                var extension = Math.Min(delay, ushort.MaxValue - _lastDelay);
                if (extension > 0)
                {
                    _lastDelay += extension;
                    delay -= extension;
                    BinaryPrimitives.WriteUInt16BigEndian(_lastControl.AsSpan(20), (ushort)_lastDelay);
                    var end = _stream.Position;
                    _stream.Position = _controlPosition;
                    WriteChunk("fcTL"u8, _lastControl);
                    _stream.Position = end;
                }

                bounds = new PixelRect(0, 0, 1, 1);
            }

            while (delay > 0)
            {
                _token.ThrowIfCancellationRequested();
                var part = Math.Min(delay, ushort.MaxValue);
                WriteFrame(pixels, bounds, part);
                delay -= part;
                bounds = new PixelRect(0, 0, 1, 1);
            }

            _stream.Flush();
        }

        private void WriteFrame(byte[] pixels, PixelRect bounds, int delay)
        {
            _lastControl.AsSpan().Clear();
            BinaryPrimitives.WriteUInt32BigEndian(_lastControl, NextSequence());
            BinaryPrimitives.WriteUInt32BigEndian(_lastControl.AsSpan(4), (uint)bounds.Width);
            BinaryPrimitives.WriteUInt32BigEndian(_lastControl.AsSpan(8), (uint)bounds.Height);
            BinaryPrimitives.WriteUInt32BigEndian(_lastControl.AsSpan(12), (uint)bounds.X);
            BinaryPrimitives.WriteUInt32BigEndian(_lastControl.AsSpan(16), (uint)bounds.Y);
            BinaryPrimitives.WriteUInt16BigEndian(_lastControl.AsSpan(20), (ushort)delay);
            BinaryPrimitives.WriteUInt16BigEndian(_lastControl.AsSpan(22), 1000);
            // Dispose NONE and blend SOURCE preserve the previous canvas while replacing this rectangle,
            // including pixels that became transparent.
            _controlPosition = _stream.Position;
            _lastDelay = delay;
            WriteChunk("fcTL"u8, _lastControl);
            using (var chunks = new PngDataChunkStream(this, _frames == 0, _token))
            using (var compressed = new ZLibStream(chunks, CompressionLevel.Optimal, true))
            {
                for (var y = 0; y < bounds.Height; y++)
                {
                    _token.ThrowIfCancellationRequested();
                    var row = _row.AsSpan(0, bounds.Width * 4 + 1);
                    row[0] = 1; // PNG Sub filter: cheap and effective on gradients and UI rows.
                    var input = pixels.AsSpan(((bounds.Y + y) * _width + bounds.X) * 4, bounds.Width * 4);
                    byte r = 0, g = 0, b = 0, a = 0;
                    for (var x = 0; x < bounds.Width; x++)
                    {
                        var offset = x * 4;
                        row[offset + 1] = unchecked((byte)(input[offset + 2] - r));
                        row[offset + 2] = unchecked((byte)(input[offset + 1] - g));
                        row[offset + 3] = unchecked((byte)(input[offset] - b));
                        row[offset + 4] = unchecked((byte)(input[offset + 3] - a));
                        r = input[offset + 2];
                        g = input[offset + 1];
                        b = input[offset];
                        a = input[offset + 3];
                    }

                    compressed.Write(row);
                }
            }

            _frames = checked(_frames + 1);
        }

        internal void WriteData(bool first, ReadOnlySpan<byte> data)
        {
            if (first) WriteChunk("IDAT"u8, data);
            else
            {
                Span<byte> sequence = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(sequence, NextSequence());
                WriteChunk("fdAT"u8, data, sequence);
            }
        }

        public void Complete()
        {
            _token.ThrowIfCancellationRequested();
            WriteChunk("IEND"u8, []);
            var end = _stream.Position;
            _stream.Position = _animationPosition;
            Span<byte> animation = stackalloc byte[8];
            animation.Clear();
            BinaryPrimitives.WriteUInt32BigEndian(animation, _frames);
            WriteChunk("acTL"u8, animation);
            _stream.Position = end;
        }

        private uint NextSequence()
        {
            var sequence = _sequence;
            _sequence = checked(sequence + 1);
            return sequence;
        }

        private void WriteChunk(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix = default)
        {
            _token.ThrowIfCancellationRequested();
            Span<byte> header = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)(prefix.Length + data.Length)));
            type.CopyTo(header[4..]);
            _stream.Write(header);
            _stream.Write(prefix);
            _stream.Write(data);
            var crc = UpdateCrc(uint.MaxValue, type);
            crc = UpdateCrc(crc, prefix);
            crc = UpdateCrc(crc, data);
            Span<byte> checksum = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(checksum, ~crc);
            _stream.Write(checksum);
        }

        private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (var value in data) crc = (crc >> 8) ^ CrcTable[(byte)(crc ^ value)];
            return crc;
        }

        private static uint[] CreateCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                var crc = i;
                for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
                table[i] = crc;
            }

            return table;
        }

        public void Dispose() => ArrayPool<byte>.Shared.Return(_row);
    }
}

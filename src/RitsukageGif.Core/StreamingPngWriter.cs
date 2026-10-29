using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace RitsukageGif.Core
{
    internal sealed class StreamingPngWriter : IDisposable
    {
        private readonly long _animationPosition;
        private readonly bool _hdr;
        private readonly uint _iterations;
        private readonly byte[] _lastControl = new byte[26];
        private readonly byte[] _row;
        private readonly Stream _stream;
        private readonly CancellationToken _token;
        private readonly int _width;
        private long _controlPosition;
        private int _lastDelay;
        private uint _sequence, _frames;

        public StreamingPngWriter(Stream stream, int width, int height, CancellationToken token, uint iterations = 0,
            bool hdr = false)
        {
            _stream = stream;
            _width = width;
            _token = token;
            _iterations = iterations;
            _hdr = hdr;
            stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);
            Span<byte> header = stackalloc byte[13];
            header.Clear();
            BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
            BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)height);
            header[8] = hdr ? (byte)16 : (byte)8;
            header[9] = 6;
            WriteChunk("IHDR"u8, header);
            if (hdr) WriteChunk("cICP"u8, [9, 16, 0, 1]);
            _animationPosition = stream.Position;
            Span<byte> animation = stackalloc byte[8];
            animation.Clear();
            WriteChunk("acTL"u8, animation);
            _row = ArrayPool<byte>.Shared.Rent(checked(width * (hdr ? 8 : 4) + 1));
        }

        public void Dispose()
        {
            ArrayPool<byte>.Shared.Return(_row);
        }

        public void Add(byte[] pixels, PixelRect bounds, int delay,
            CaptureAlphaMode alphaMode = CaptureAlphaMode.Straight)
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

                bounds = new(0, 0, 1, 1);
            }

            while (delay > 0)
            {
                _token.ThrowIfCancellationRequested();
                var part = Math.Min(delay, ushort.MaxValue);
                WriteFrame(pixels, bounds, part, alphaMode);
                delay -= part;
                bounds = new(0, 0, 1, 1);
            }

            _stream.Flush();
        }

        private void WriteFrame(byte[] pixels, PixelRect bounds, int delay, CaptureAlphaMode alphaMode)
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
                for (var y = 0; y < bounds.Height; y++)
                {
                    _token.ThrowIfCancellationRequested();
                    var bytesPerPixel = _hdr ? 8 : 4;
                    var row = _row.AsSpan(0, bounds.Width * bytesPerPixel + 1);
                    row[0] = 1; // PNG Sub filter: cheap and effective on gradients and UI rows.
                    var input = pixels.AsSpan(((bounds.Y + y) * _width + bounds.X) * bytesPerPixel,
                        bounds.Width * bytesPerPixel);
                    if (_hdr)
                    {
                        HdrPngColor.EncodeRow(input, row[1..], alphaMode);
                        for (var offset = row.Length - 1; offset > bytesPerPixel; offset--)
                            row[offset] = unchecked((byte)(row[offset] - row[offset - bytesPerPixel]));
                    }
                    else
                    {
                        FilterRow(input, row[1..]);
                    }

                    compressed.Write(row);
                }

            _frames = checked(_frames + 1);
        }

        private static void FilterRow(ReadOnlySpan<byte> input, Span<byte> output)
        {
            output[0] = input[2];
            output[1] = input[1];
            output[2] = input[0];
            output[3] = input[3];
            var offset = 4;
            if (Vector128.IsHardwareAccelerated)
            {
                var order = Vector128.Create((byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);
                ref var source = ref MemoryMarshal.GetReference(input);
                ref var target = ref MemoryMarshal.GetReference(output);
                for (; offset <= input.Length - Vector128<byte>.Count; offset += Vector128<byte>.Count)
                {
                    var current = Vector128.LoadUnsafe(ref source, (nuint)offset);
                    var previous = Vector128.LoadUnsafe(ref source, (nuint)(offset - 4));
                    Vector128.Shuffle(current - previous, order).StoreUnsafe(ref target, (nuint)offset);
                }
            }

            for (; offset < input.Length; offset += 4)
            {
                output[offset] = unchecked((byte)(input[offset + 2] - input[offset - 2]));
                output[offset + 1] = unchecked((byte)(input[offset + 1] - input[offset - 3]));
                output[offset + 2] = unchecked((byte)(input[offset] - input[offset - 4]));
                output[offset + 3] = unchecked((byte)(input[offset + 3] - input[offset - 1]));
            }
        }

        internal void WriteData(bool first, ReadOnlySpan<byte> data)
        {
            if (first)
            {
                WriteChunk("IDAT"u8, data);
            }
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
            BinaryPrimitives.WriteUInt32BigEndian(animation[4..], _iterations);
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
            var crc = PngCrc32.Update(uint.MaxValue, type);
            crc = PngCrc32.Update(crc, prefix);
            crc = PngCrc32.Update(crc, data);
            Span<byte> checksum = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(checksum, ~crc);
            _stream.Write(checksum);
        }
    }
}

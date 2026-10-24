using System.Buffers.Binary;

namespace RitsukageGif.Core
{
    internal sealed class WebpAnimationWriter
    {
        private const int MaximumDelay = 0xffffff;
        private readonly Stream _stream;
        private long _delayOffset;
        private int _delay;

        public WebpAnimationWriter(Stream stream, int width, int height, uint iterations = 0)
        {
            _stream = stream;
            stream.Write("RIFF"u8);
            WriteUInt32(0);
            stream.Write("WEBPVP8X"u8);
            WriteUInt32(10);
            stream.Write([0x12, 0, 0, 0]);
            WriteUInt24(width - 1);
            WriteUInt24(height - 1);
            stream.Write("ANIM"u8);
            WriteUInt32(6);
            stream.Write([0, 0, 0, 0]);
            Span<byte> loop = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(loop, checked((ushort)iterations));
            stream.Write(loop);
        }

        public void Add(byte[] webp, PixelRect bounds, int delay, bool blend = false)
        {
            var data = ImageChunk(webp);
            do
            {
                var part = Math.Min(delay, MaximumDelay);
                if (_stream.Position + 24 + data.Length > uint.MaxValue - 1L)
                    throw new IOException(UiText.Get("RecordingTooLarge"));
                _stream.Write("ANMF"u8);
                WriteUInt32(checked((uint)(16 + data.Length)));
                WriteUInt24(bounds.X / 2);
                WriteUInt24(bounds.Y / 2);
                WriteUInt24(bounds.Width - 1);
                WriteUInt24(bounds.Height - 1);
                _delayOffset = _stream.Position;
                _delay = part;
                WriteUInt24(part);
                // Replace the rectangle, including transparent pixels; retain the rest of the canvas.
                _stream.WriteByte(blend ? (byte)0 : (byte)2);
                _stream.Write(data);
                delay -= part;
            } while (delay > 0);
        }

        public void Extend(byte[] webp, PixelRect bounds, int delay, bool blend = false)
        {
            var extra = Math.Min(MaximumDelay - _delay, delay);
            var end = _stream.Position;
            _stream.Position = _delayOffset;
            _delay += extra;
            WriteUInt24(_delay);
            _stream.Position = end;
            if (delay > extra) Add(webp, bounds, delay - extra, blend);
        }

        public void Complete()
        {
            var end = _stream.Position;
            _stream.Position = 4;
            WriteUInt32(checked((uint)(end - 8)));
            _stream.Position = end;
            _stream.Flush();
        }

        private static ReadOnlySpan<byte> ImageChunk(byte[] webp)
        {
            if (webp.Length < 12 || !webp.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
                !webp.AsSpan(8, 4).SequenceEqual("WEBP"u8)) throw new InvalidDataException();
            var offset = 12;
            var alphaOffset = -1;
            var alphaEnd = -1;
            while (offset <= webp.Length - 8)
            {
                var length = BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(offset + 4, 4));
                var padded = (long)length + (length & 1);
                if (padded > webp.Length - offset - 8) throw new InvalidDataException();
                if (webp.AsSpan(offset, 4).SequenceEqual("VP8L"u8))
                    return webp.AsSpan(offset, checked(8 + (int)padded));
                if (webp.AsSpan(offset, 4).SequenceEqual("ALPH"u8))
                {
                    alphaOffset = offset;
                    alphaEnd = checked(offset + 8 + (int)padded);
                }

                if (webp.AsSpan(offset, 4).SequenceEqual("VP8 "u8))
                {
                    if (alphaOffset < 0) return webp.AsSpan(offset, checked(8 + (int)padded));
                    if (alphaEnd != offset) throw new InvalidDataException();
                    return webp.AsSpan(alphaOffset, checked(offset - alphaOffset + 8 + (int)padded));
                }

                offset += checked(8 + (int)padded);
            }

            throw new InvalidDataException();
        }

        private void WriteUInt24(int value)
        {
            _stream.WriteByte((byte)value);
            _stream.WriteByte((byte)(value >> 8));
            _stream.WriteByte((byte)(value >> 16));
        }

        private void WriteUInt32(uint value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            _stream.Write(bytes);
        }
    }
}

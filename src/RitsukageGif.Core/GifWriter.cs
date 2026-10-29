using System.Buffers.Binary;

namespace RitsukageGif.Core
{
    internal sealed class GifWriter : IDisposable
    {
        private readonly bool _clearCanvas;
        private readonly Stream _stream;
        private int _delay;
        private long _delayOffset;

        public GifWriter(Stream stream, int width, int height, bool clearCanvas = false, uint iterations = 0)
        {
            _clearCanvas = clearCanvas;
            _stream = stream;
            _stream.Write("GIF89a"u8);
            WriteWord(width);
            WriteWord(height);
            _stream.Write("p\0\0"u8);
            if (iterations != 1)
            {
                _stream.Write([0x21, 0xff, 11]);
                _stream.Write("NETSCAPE2.0"u8);
                _stream.Write([3, 1]);
                WriteWord(checked((ushort)(iterations == 0 ? 0 : iterations - 1)));
                _stream.WriteByte(0);
            }
        }

        public void Dispose()
        {
            _stream.WriteByte(0x3b);
        }

        public void Add(byte[] gif, PixelRect bounds, int delay)
        {
            do
            {
                var part = Math.Min(65535, delay);
                if (delay - part == 1) part--;
                AddFrame(gif, bounds, part);
                delay -= part;
            } while (delay > 0);
        }

        public void Extend(byte[] gif, PixelRect bounds, int delay)
        {
            var extra = Math.Min(65535 - _delay, delay);
            if (delay - extra == 1) extra--;
            _delay += extra;
            var end = _stream.Position;
            _stream.Position = _delayOffset;
            WriteWord(_delay);
            _stream.Position = end;
            if (delay > extra) Add(gif, bounds, delay - extra);
        }

        private void AddFrame(byte[] gif, PixelRect bounds, int delay)
        {
            var paletteFlags = gif[10] & 7;
            var paletteSize = (gif[10] & 0x80) != 0 ? 3 * (2 << paletteFlags) : 0;
            var paletteOffset = 13;
            var at = 13 + paletteSize;
            byte transparent = 0, transparency = 0;
            while (gif[at] == 0x21)
            {
                if (gif[at + 1] == 0xf9)
                {
                    transparency = (byte)(gif[at + 3] & 1);
                    transparent = gif[at + 6];
                }

                at += 2;
                while (gif[at] != 0) at += gif[at] + 1;
                at++;
            }

            if (gif[at] != 0x2c) throw new InvalidDataException();
            var descriptorFlags = gif[at + 9];
            at += 10;
            if ((descriptorFlags & 0x80) != 0)
            {
                paletteFlags = descriptorFlags & 7;
                paletteSize = 3 * (2 << paletteFlags);
                paletteOffset = at;
                at += paletteSize;
            }

            if (paletteSize == 0) throw new InvalidDataException();
            _stream.Write([0x21, 0xf9, 4, (byte)((_clearCanvas ? 8 : 4) | transparency)]);
            _delayOffset = _stream.Position;
            _delay = delay;
            WriteWord(delay);
            _stream.Write([transparent, 0, 0x2c]);
            WriteWord(bounds.X);
            WriteWord(bounds.Y);
            WriteWord(bounds.Width);
            WriteWord(bounds.Height);
            _stream.WriteByte((byte)(0x80 | (descriptorFlags & 0x40) | paletteFlags));
            _stream.Write(gif.AsSpan(paletteOffset, paletteSize));
            _stream.WriteByte(gif[at++]);
            do
            {
                var length = gif[at];
                _stream.Write(gif.AsSpan(at, length + 1));
                at += length + 1;
                if (length == 0) break;
            } while (at < gif.Length);
        }

        private void WriteWord(int value)
        {
            Span<byte> bytes = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, checked((ushort)value));
            _stream.Write(bytes);
        }
    }
}

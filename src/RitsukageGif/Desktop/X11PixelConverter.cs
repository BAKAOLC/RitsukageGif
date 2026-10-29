using System.Numerics;

namespace RitsukageGif.Desktop
{
    internal static class X11PixelConverter
    {
        public static byte[] Convert(ReadOnlySpan<byte> source, int width, int height, int stride,
            int bitsPerPixel, bool littleEndian, ulong red, ulong green, ulong blue, bool alpha)
        {
            var pixelBytes = bitsPerPixel / 8;
            if (width <= 0 || height <= 0 || width > 32768 || height > 32768 ||
                bitsPerPixel is not (8 or 16 or 24 or 32) || stride < (long)width * pixelBytes ||
                (long)stride * height > source.Length || (long)width * height * 4 > 512L * 1024 * 1024 ||
                red == 0 || green == 0 || blue == 0 || (red | green | blue) >> bitsPerPixel != 0 ||
                (red & green) != 0 || (red & blue) != 0 || (green & blue) != 0)
                throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
            var redShift = BitOperations.TrailingZeroCount(red);
            var greenShift = BitOperations.TrailingZeroCount(green);
            var blueShift = BitOperations.TrailingZeroCount(blue);
            var pixels = new byte[checked(width * height * 4)];
            for (var row = 0; row < height; row++)
            {
                var input = source.Slice(row * stride, width * pixelBytes);
                var output = pixels.AsSpan(row * width * 4, width * 4);
                if (!alpha && pixelBytes == 4 && littleEndian && red == 0xff0000 && green == 0xff00 && blue == 0xff)
                {
                    input.CopyTo(output);
                    for (var index = 3; index < output.Length; index += 4) output[index] = 255;
                    continue;
                }

                for (var col = 0; col < width; col++)
                {
                    uint value = 0;
                    for (var channel = 0; channel < pixelBytes; channel++)
                        value |= (uint)input[col * pixelBytes + channel] <<
                                 ((littleEndian ? channel : pixelBytes - channel - 1) * 8);
                    var index = col * 4;
                    var opacity = alpha && bitsPerPixel == 32 ? (byte)(value >> 24) : (byte)255;
                    output[index] = Unpremultiply(Channel(value, blue, blueShift), opacity);
                    output[index + 1] = Unpremultiply(Channel(value, green, greenShift), opacity);
                    output[index + 2] = Unpremultiply(Channel(value, red, redShift), opacity);
                    output[index + 3] = opacity;
                }
            }

            return pixels;
        }

        private static byte Channel(uint value, ulong mask, int shift)
        {
            return (byte)(((value & mask) >> shift) * 255 / (mask >> shift));
        }

        private static byte Unpremultiply(byte value, byte alpha)
        {
            return alpha switch
            {
                0 => 0,
                255 => value,
                _ => (byte)Math.Min(255, (value * 255 + alpha / 2) / alpha),
            };
        }
    }
}

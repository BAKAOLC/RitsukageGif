using System.Buffers.Binary;

namespace RitsukageGif.Desktop
{
    internal readonly struct MagnifierPixelSource(
        ReadOnlyMemory<byte> pixels,
        PixelRect bounds,
        int stride,
        int format = 0,
        CaptureAlphaMode alphaMode = CaptureAlphaMode.Straight)
    {
        public PixelRect Bounds { get; } = bounds;

        public double Read(int x, int y, int channel)
        {
            if (channel == 3 && alphaMode == CaptureAlphaMode.Opaque) return 1;
            var index = (y - Bounds.Y) * stride + (x - Bounds.X) * (format == 0 ? 4 : 8);
            switch (format)
            {
                case 0:
                    return pixels.Span[index + (channel == 3 ? 3 : 2 - channel)] / 255d;
                case 1:
                    return BinaryPrimitives.ReadUInt16BigEndian(pixels.Span[(index + channel * 2)..]) / 65535d;
            }

            var span = pixels.Span;
            var value = (double)BitConverter.UInt16BitsToHalf(
                BinaryPrimitives.ReadUInt16LittleEndian(span[(index + channel * 2)..]));
            if (channel == 3 || alphaMode != CaptureAlphaMode.Premultiplied) return value;
            var alpha = (double)BitConverter.UInt16BitsToHalf(
                BinaryPrimitives.ReadUInt16LittleEndian(span[(index + 6)..]));
            return alpha > 0 ? value / alpha : 0;
        }
    }
}

using System.Buffers.Binary;

namespace RitsukageGif.Desktop
{
    internal readonly struct PinnedViewportFrameSource(CapturedFrame source) : IPinnedViewportSource
    {
        public int Width => source.Width;
        public int Height => source.Height;

        public PinnedViewportSample Read(int x, int y)
        {
            var offset = y * source.Stride + x * 8;
            var row = source.Pixels.Span[offset..];
            var alpha = source.AlphaMode == CaptureAlphaMode.Opaque
                ? 1d
                : (double)BinaryPrimitives.ReadHalfLittleEndian(row[6..]);
            if (alpha == 0) return default;
            var multiplier = source.AlphaMode == CaptureAlphaMode.Straight ? alpha : 1;
            return new((double)BinaryPrimitives.ReadHalfLittleEndian(row) * multiplier,
                (double)BinaryPrimitives.ReadHalfLittleEndian(row[2..]) * multiplier,
                (double)BinaryPrimitives.ReadHalfLittleEndian(row[4..]) * multiplier, alpha);
        }
    }
}

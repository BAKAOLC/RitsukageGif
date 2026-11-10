using System.Buffers.Binary;

namespace RitsukageGif.Desktop
{
    internal readonly struct PinnedViewportPreviewSource(CapturedFrame source) : IPinnedViewportSource
    {
        public int Width => source.Width;
        public int Height => source.Height;

        public PinnedViewportSample Read(int x, int y)
        {
            var bytes = source.Pixels.Span[(y * source.Stride + x * 8)..];
            var alpha = source.AlphaMode == CaptureAlphaMode.Opaque
                ? 1d
                : (double)BinaryPrimitives.ReadHalfLittleEndian(bytes[6..]);
            if (alpha == 0) return default;
            var white = source.ColorInfo.SdrWhiteNits;
            for (var index = source.ColorRegions.Count - 1; index >= 0; index--)
            {
                var region = source.ColorRegions[index];
                if (region.ColorInfo == source.ColorInfo || !region.Region.Contains(new(x, y))) continue;
                white = region.ColorInfo.SdrWhiteNits;
                break;
            }

            var multiplier = source.ColorInfo.SdrWhiteNits / white *
                             (source.AlphaMode == CaptureAlphaMode.Straight ? alpha : 1);
            return new((double)BinaryPrimitives.ReadHalfLittleEndian(bytes) * multiplier,
                (double)BinaryPrimitives.ReadHalfLittleEndian(bytes[2..]) * multiplier,
                (double)BinaryPrimitives.ReadHalfLittleEndian(bytes[4..]) * multiplier, alpha);
        }
    }
}

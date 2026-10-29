using System.Runtime.InteropServices;

namespace RitsukageGif.Core
{
    public static class HdrToneMapping
    {
        public static CapturedFrame Map(CapturedFrame source,
            HdrToneMapMode mode = HdrToneMapMode.HighlightCompression, CancellationToken token = default)
        {
            return Map(source, mode, null, token);
        }

        internal static CapturedFrame Map(CapturedFrame source, HdrToneMapMode mode,
            byte[]? destination, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (mode is not (HdrToneMapMode.HighlightCompression or HdrToneMapMode.ReferenceWhite))
                throw new ArgumentOutOfRangeException(nameof(mode));
            if (source.PixelFormat != CapturePixelFormat.Rgba16FloatScRgb || source.Width <= 0 || source.Height <= 0 ||
                source.Stride < (long)source.Width * 8 || source.Pixels.Length < (long)source.Stride * source.Height)
                throw new ArgumentException("A valid linear scRGB FP16 frame is required.", nameof(source));
            token.ThrowIfCancellationRequested();
            var length = checked(source.Width * source.Height * 4);
            if (destination is not null && destination.Length != length)
                throw new ArgumentException("The destination must match the frame dimensions.", nameof(destination));
            var output = destination ?? new byte[length];
            IReadOnlyList<FrameColorRegion> regions =
            [
                new(new(0, 0, source.Width, source.Height), source.ColorInfo),
                .. source.ColorRegions.Where(region => region.ColorInfo != source.ColorInfo),
            ];
            foreach (var region in regions)
            {
                var clipped = region.Region.Intersect(new(0, 0, source.Width, source.Height));
                if (clipped.IsEmpty) continue;
                Parallel.For(clipped.Y, clipped.Bottom, new() { CancellationToken = token }, y =>
                {
                    ScRgbToneMapper.Convert(MemoryMarshal.Cast<byte, Half>(source.Pixels.Span.Slice(
                            y * source.Stride + clipped.X * 8, clipped.Width * 8)),
                        output.AsSpan((y * source.Width + clipped.X) * 4, clipped.Width * 4),
                        region.ColorInfo.SdrWhiteNits / FrameColorInfo.ScRgbReferenceWhiteNits,
                        region.ColorInfo.IsHdrSource, mode, source.AlphaMode, true);
                });
            }

            return source with
            {
                PixelFormat = CapturePixelFormat.Bgra8Srgb, Stride = checked(source.Width * 4), Pixels = output,
                AlphaMode = source.AlphaMode == CaptureAlphaMode.Opaque
                    ? CaptureAlphaMode.Opaque
                    : CaptureAlphaMode.Straight,
            };
        }
    }
}

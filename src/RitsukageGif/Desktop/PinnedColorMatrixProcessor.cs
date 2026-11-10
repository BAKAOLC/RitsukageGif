using System.Buffers.Binary;
using System.Numerics;

namespace RitsukageGif.Desktop
{
    internal static class PinnedColorMatrixProcessor
    {
        internal static CapturedFrame Apply(CapturedFrame source, PinnedColorMatrixState state,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (state.Matrix is not { } matrix) return source;
            if (source.PixelFormat is not (CapturePixelFormat.Bgra8Srgb or CapturePixelFormat.Rgba16FloatScRgb))
                throw new ArgumentException("An SDR BGRA8 or linear scRGB FP16 frame is required.", nameof(source));
            var pixelSize = source.PixelFormat == CapturePixelFormat.Bgra8Srgb ? 4 : 8;
            if (source.Width <= 0 || source.Height <= 0 || source.Stride < (long)source.Width * pixelSize ||
                source.Pixels.Length < (long)source.Stride * source.Height)
                throw new ArgumentException("The source frame has invalid dimensions.", nameof(source));

            var output = source.Pixels.Span[..checked(source.Stride * source.Height)].ToArray();
            if (source.PixelFormat == CapturePixelFormat.Bgra8Srgb)
            {
                for (var y = 0; y < source.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    ApplyBgra8(source.Pixels.Span.Slice(y * source.Stride, source.Width * 4),
                        output.AsSpan(y * source.Stride, source.Width * 4), matrix, source.AlphaMode);
                }
            }
            else
            {
                ApplyHdrRegion(source, output, matrix, new(0, 0, source.Width, source.Height),
                    source.ColorInfo, token);
                foreach (var region in source.ColorRegions)
                    ApplyHdrRegion(source, output, matrix,
                        region.Region.Intersect(new(0, 0, source.Width, source.Height)), region.ColorInfo, token);
            }

            token.ThrowIfCancellationRequested();
            return source with { Pixels = output };
        }

        internal static void ApplyBgra8(ReadOnlySpan<byte> source, Span<byte> destination,
            RgbColorMatrix matrix, CaptureAlphaMode alphaMode)
        {
            for (var offset = 0; offset < source.Length; offset += 4)
            {
                var pixel = source.Slice(offset, 4);
                var alpha = alphaMode == CaptureAlphaMode.Opaque ? 1 : pixel[3] / 255f;
                var value = new Vector3(pixel[2], pixel[1], pixel[0]);
                if (alphaMode == CaptureAlphaMode.Premultiplied) value = alpha > 0 ? value / alpha : Vector3.Zero;
                value = matrix.Transform(value);
                if (alphaMode == CaptureAlphaMode.Premultiplied) value *= alpha;
                destination[offset] = Quantize(value.Z);
                destination[offset + 1] = Quantize(value.Y);
                destination[offset + 2] = Quantize(value.X);
                destination[offset + 3] = pixel[3];
            }
        }

        private static void ApplyRgba16Float(ReadOnlySpan<byte> source, Span<byte> destination,
            RgbColorMatrix matrix, CaptureAlphaMode alphaMode, float whiteLevel)
        {
            for (var offset = 0; offset < source.Length; offset += 8)
            {
                var pixel = source.Slice(offset, 8);
                var alpha = alphaMode == CaptureAlphaMode.Opaque ? 1 : ReadHalf(pixel[6..]);
                var value = new Vector3(ReadHalf(pixel), ReadHalf(pixel[2..]), ReadHalf(pixel[4..]));
                if (alphaMode == CaptureAlphaMode.Premultiplied) value = alpha > 0 ? value / alpha : Vector3.Zero;
                value = matrix.Transform(value, whiteLevel / 255f);
                if (alphaMode == CaptureAlphaMode.Premultiplied) value *= alpha;
                StoreHalf(destination[offset..], value.X);
                StoreHalf(destination[(offset + 2)..], value.Y);
                StoreHalf(destination[(offset + 4)..], value.Z);
                pixel[6..].CopyTo(destination[(offset + 6)..]);
            }
        }

        private static void ApplyHdrRegion(CapturedFrame source, byte[] destination, RgbColorMatrix matrix,
            PixelRect region, FrameColorInfo colorInfo, CancellationToken token)
        {
            for (var y = region.Y; y < region.Bottom; y++)
            {
                token.ThrowIfCancellationRequested();
                var offset = y * source.Stride + region.X * 8;
                ApplyRgba16Float(source.Pixels.Span.Slice(offset, region.Width * 8),
                    destination.AsSpan(offset, region.Width * 8), matrix, source.AlphaMode,
                    colorInfo.SdrWhiteNits / FrameColorInfo.ScRgbReferenceWhiteNits);
            }
        }

        private static byte Quantize(float value)
        {
            return (byte)Math.Clamp(MathF.Round(value, MidpointRounding.ToEven), 0, 255);
        }

        private static float ReadHalf(ReadOnlySpan<byte> source)
        {
            return (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(source));
        }

        private static void StoreHalf(Span<byte> destination, float value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination,
                BitConverter.HalfToUInt16Bits((Half)Math.Clamp(value, -65504, 65504)));
        }
    }
}

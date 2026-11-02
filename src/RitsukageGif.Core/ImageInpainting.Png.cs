using System.Buffers.Binary;
using System.Numerics;
using SkiaSharp;

namespace RitsukageGif.Core
{
    public static partial class ImageInpainting
    {
        public static PngRaster RestorePng(PngRaster source, ushort[] coverage, int radius = 4,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(coverage);
            var raster = PngColorTransform.NormalizeAnnotationRaster(source, token);
            if (coverage.Length != (long)raster.Width * raster.Height)
                throw new ArgumentException("The image and inpainting mask dimensions must match.", nameof(coverage));
            var region = MaskRegion(coverage, raster.Width, raster.Height, radius, token);
            if (region.IsEmpty) return raster;
            if (region.Width != raster.Width || region.Height != raster.Height)
            {
                var cropped = new byte[checked(region.Width * region.Height * 8)];
                for (var y = 0; y < region.Height; y++)
                    Buffer.BlockCopy(raster.Samples, ((region.Y + y) * raster.Width + region.X) * 8,
                        cropped, y * region.Width * 8, region.Width * 8);
                var repaired = RestorePng(
                    raster with { Width = region.Width, Height = region.Height, Samples = cropped },
                    CropCoverage(coverage, raster.Width, region), radius, token);
                token.ThrowIfCancellationRequested();
                var output = raster.Samples.ToArray();
                for (var y = 0; y < region.Height; y++)
                    Buffer.BlockCopy(repaired.Samples, y * region.Width * 8, output,
                        ((region.Y + y) * raster.Width + region.X) * 8, region.Width * 8);
                return raster with { Samples = output };
            }

            var cicp = raster.Metadata.FirstOrDefault(chunk => chunk.Type == "cICP").Data;
            var pq = cicp.Length == 4 && cicp.Span[1] == 16;
            using var sourceSpace =
                pq ? PqPrimaries(cicp.Span) : PngColorTransform.CreateSourceSpace(raster.Metadata, token);
            using var linearSpace = SKColorSpace.CreateSrgbLinear();
            var encoded = new Vector4[coverage.Length];
            for (var index = 0; index < encoded.Length; index++)
            {
                if ((index & 4095) == 0) token.ThrowIfCancellationRequested();
                var input = raster.Samples.AsSpan(index * 8, 8);
                var red = BinaryPrimitives.ReadUInt16BigEndian(input) / 65535f;
                var green = BinaryPrimitives.ReadUInt16BigEndian(input[2..]) / 65535f;
                var blue = BinaryPrimitives.ReadUInt16BigEndian(input[4..]) / 65535f;
                encoded[index] = new(pq ? HdrPngColor.DecodePq(red) : red,
                    pq ? HdrPngColor.DecodePq(green) : green,
                    pq ? HdrPngColor.DecodePq(blue) : blue,
                    BinaryPrimitives.ReadUInt16BigEndian(input[6..]) / 65535f);
            }

            var linear = new Vector4[encoded.Length];
            ConvertColors(encoded, linear, raster.Width, raster.Height, sourceSpace, SKAlphaType.Unpremul,
                linearSpace, SKAlphaType.Premul);
            Restore(linear, raster.Width, raster.Height, coverage, radius, token);
            ConvertColors(linear, encoded, raster.Width, raster.Height, linearSpace, SKAlphaType.Premul,
                sourceSpace, SKAlphaType.Unpremul);
            var result = raster.Samples.ToArray();
            Span<byte> restored = stackalloc byte[8];
            for (var index = 0; index < encoded.Length; index++)
            {
                if ((index & 4095) == 0) token.ThrowIfCancellationRequested();
                var amount = BitOperations.PopCount(coverage[index]);
                if (amount == 0) continue;
                var value = encoded[index];
                BinaryPrimitives.WriteUInt16BigEndian(restored,
                    Component16(pq ? HdrPngColor.EncodePq(value.X) : value.X));
                BinaryPrimitives.WriteUInt16BigEndian(restored[2..],
                    Component16(pq ? HdrPngColor.EncodePq(value.Y) : value.Y));
                BinaryPrimitives.WriteUInt16BigEndian(restored[4..],
                    Component16(pq ? HdrPngColor.EncodePq(value.Z) : value.Z));
                BinaryPrimitives.WriteUInt16BigEndian(restored[6..], Component16(value.W));
                var target = result.AsSpan(index * 8, 8);
                if (amount == 16)
                {
                    restored.CopyTo(target);
                    continue;
                }

                var front = (ulong)BinaryPrimitives.ReadUInt16BigEndian(restored[6..]) * (uint)amount;
                var back = (ulong)BinaryPrimitives.ReadUInt16BigEndian(target[6..]) * (uint)(16 - amount);
                var weight = front + back;
                for (var channel = 0; channel < 3; channel++)
                    BinaryPrimitives.WriteUInt16BigEndian(target[(channel * 2)..], weight > 0
                        ? (ushort)((BinaryPrimitives.ReadUInt16BigEndian(restored[(channel * 2)..]) * front +
                                    BinaryPrimitives.ReadUInt16BigEndian(target[(channel * 2)..]) * back + weight / 2) /
                                   weight)
                        : (ushort)0);
                BinaryPrimitives.WriteUInt16BigEndian(target[6..], (ushort)((weight + 8) / 16));
            }

            return raster with { Samples = result };
        }

        private static SKColorSpace PqPrimaries(ReadOnlySpan<byte> cicp)
        {
            if (cicp[2] != 0 || cicp[3] != 1) throw new NotSupportedException("Unsupported HDR PNG signal range.");
            var primaries = cicp[0] switch
            {
                1 => SKColorSpaceXyz.Srgb, 9 => SKColorSpaceXyz.Rec2020, 12 => SKColorSpaceXyz.DisplayP3,
                _ => throw new NotSupportedException("Unsupported HDR PNG color primaries."),
            };
            return SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Linear, primaries);
        }

        private static unsafe void ConvertColors(Vector4[] source, Vector4[] destination, int width, int height,
            SKColorSpace sourceSpace, SKAlphaType sourceAlpha, SKColorSpace destinationSpace,
            SKAlphaType destinationAlpha)
        {
            fixed (Vector4* input = source)
            fixed (Vector4* output = destination)
            {
                using var pixmap = new SKPixmap(new(width, height, SKColorType.RgbaF32, sourceAlpha,
                    sourceSpace), (nint)input, width * 16);
                if (!pixmap.ReadPixels(new(width, height, SKColorType.RgbaF32, destinationAlpha,
                        destinationSpace), (nint)output, width * 16))
                    throw new InvalidOperationException("The inpainting color conversion failed.");
            }
        }

        private static ushort Component16(float value)
        {
            return (ushort)Math.Clamp(MathF.Round(value * 65535), 0, 65535);
        }
    }
}

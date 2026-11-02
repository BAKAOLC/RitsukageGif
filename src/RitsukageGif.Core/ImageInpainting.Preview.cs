using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace RitsukageGif.Core
{
    public static partial class ImageInpainting
    {
        internal static byte[] ToSdrBgra(PngRaster raster, CancellationToken token)
        {
            var cicp = raster.Metadata.FirstOrDefault(chunk => chunk.Type == "cICP").Data;
            if (cicp.Length != 4 || cicp.Span[1] != 16)
            {
                var srgb = PngColorTransform.ConvertToSrgb(raster.Samples, raster.Width, raster.Height, 16, 6,
                    raster.Metadata, token);
                var output = new byte[checked(raster.Width * raster.Height * 4)];
                for (var index = 0; index < output.Length / 4; index++)
                {
                    if ((index & 4095) == 0) token.ThrowIfCancellationRequested();
                    for (var channel = 0; channel < 4; channel++)
                        output[index * 4 + channel] = (byte)((BinaryPrimitives.ReadUInt16BigEndian(
                            srgb.AsSpan(index * 8 + (channel < 3 ? 2 - channel : 3) * 2)) + 128) / 257);
                }

                return output;
            }

            var encoded = new Vector4[checked(raster.Width * raster.Height)];
            for (var index = 0; index < encoded.Length; index++)
            {
                if ((index & 4095) == 0) token.ThrowIfCancellationRequested();
                var sample = raster.Samples.AsSpan(index * 8, 8);
                encoded[index] = new(HdrPngColor.DecodePq(BinaryPrimitives.ReadUInt16BigEndian(sample) / 65535f),
                    HdrPngColor.DecodePq(BinaryPrimitives.ReadUInt16BigEndian(sample[2..]) / 65535f),
                    HdrPngColor.DecodePq(BinaryPrimitives.ReadUInt16BigEndian(sample[4..]) / 65535f),
                    BinaryPrimitives.ReadUInt16BigEndian(sample[6..]) / 65535f);
            }

            using var sourceSpace = PqPrimaries(cicp.Span);
            using var linearSpace = SKColorSpace.CreateSrgbLinear();
            var linear = new Vector4[encoded.Length];
            ConvertColors(encoded, linear, raster.Width, raster.Height, sourceSpace, SKAlphaType.Unpremul,
                linearSpace, SKAlphaType.Premul);
            var samples = new byte[checked(linear.Length * 8)];
            var half = MemoryMarshal.Cast<byte, Half>(samples.AsSpan());
            for (var index = 0; index < linear.Length; index++)
            {
                if ((index & 4095) == 0) token.ThrowIfCancellationRequested();
                half[index * 4] = (Half)linear[index].X;
                half[index * 4 + 1] = (Half)linear[index].Y;
                half[index * 4 + 2] = (Half)linear[index].Z;
                half[index * 4 + 3] = (Half)linear[index].W;
            }

            return HdrToneMapping.Map(new(raster.Width, raster.Height, raster.Width * 8,
                    CapturePixelFormat.Rgba16FloatScRgb, samples, new(true), CaptureAlphaMode.Premultiplied, 0, []),
                token: token).Pixels.ToArray();
        }
    }
}

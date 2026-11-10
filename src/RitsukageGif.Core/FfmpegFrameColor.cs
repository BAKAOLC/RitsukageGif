using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using SkiaSharp;

namespace RitsukageGif.Core
{
    internal sealed record FfmpegFrameColor(
        string Transfer,
        string Primaries,
        string Matrix,
        bool RawHalf,
        CaptureAlphaMode Alpha,
        float PeakNits)
    {
        private static readonly Lazy<float[]> PqValues = new(() => Lookup(HdrPngColor.DecodePq));
        private static readonly Lazy<float[]> HlgValues = new(() => Lookup(Hlg));
        public bool IsHdr => RawHalf || Transfer is "smpte2084" or "arib-std-b67";

        public static FfmpegFrameColor Read(JsonElement stream)
        {
            var raw = Tag("rg_pixel_encoding") == "scrgb-f16-bits";
            var alpha = Tag("rg_alpha_mode") switch
            {
                "Opaque" => CaptureAlphaMode.Opaque,
                "Premultiplied" => CaptureAlphaMode.Premultiplied,
                "Straight" => CaptureAlphaMode.Straight,
                _ => Text("pix_fmt").Contains('a') ? CaptureAlphaMode.Straight : CaptureAlphaMode.Opaque,
            };
            return new(Text("color_transfer"), Text("color_primaries"), Text("color_space"), raw, alpha, 1000);

            string Text(string name)
            {
                return stream.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
            }

            string Tag(string name)
            {
                if (!stream.TryGetProperty("tags", out var tags)) return "";
                foreach (var tag in tags.EnumerateObject())
                    if (tag.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return tag.Value.GetString() ?? "";
                return "";
            }
        }

        public byte[] DecodeLinear(byte[] encoded, int width, int height, int outputWidth, int outputHeight,
            CancellationToken token)
        {
            var linear = RawHalf ? encoded : new byte[encoded.Length];
            if (!RawHalf)
            {
                var curve = Transfer == "smpte2084" ? PqValues.Value : HlgValues.Value;
                Parallel.For(0, height, new() { CancellationToken = token }, y =>
                {
                    for (var offset = y * width * 8; offset < (y + 1) * width * 8; offset += 8)
                    {
                        var r = curve[BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(offset))];
                        var g = curve[BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(offset + 2))];
                        var b = curve[BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(offset + 4))];
                        var a = BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(offset + 6)) / 65535f;
                        Vector3 value;
                        if (Transfer == "smpte2084")
                        {
                            value = new(r, g, b);
                        }
                        else
                        {
                            value = new(r, g, b);
                            var weights = Primaries switch
                            {
                                "bt709" => new Vector3(.2126f, .7152f, .0722f),
                                "smpte432" => new Vector3(.2289746f, .6917385f, .0792869f),
                                _ => new Vector3(.2627f, .678f, .0593f),
                            };
                            var luminance = Vector3.Dot(value, weights);
                            var gamma = 1.2f + .42f * MathF.Log10(PeakNits / 1000);
                            value *= PeakNits / 80 * MathF.Pow(Math.Max(0, luminance), gamma - 1);
                        }

                        value = Primaries switch
                        {
                            "bt709" => value,
                            "smpte432" => new(1.224745f * value.X - .224904f * value.Y,
                                -.042058f * value.X + 1.042081f * value.Y,
                                -.019642f * value.X - .078655f * value.Y + 1.098537f * value.Z),
                            _ => new(1.660491f * value.X - .587641f * value.Y - .07285f * value.Z,
                                -.12455f * value.X + 1.1329f * value.Y - .00835f * value.Z,
                                -.018151f * value.X - .100579f * value.Y + 1.11873f * value.Z),
                        };
                        BinaryPrimitives.WriteHalfLittleEndian(linear.AsSpan(offset), (Half)value.X);
                        BinaryPrimitives.WriteHalfLittleEndian(linear.AsSpan(offset + 2), (Half)value.Y);
                        BinaryPrimitives.WriteHalfLittleEndian(linear.AsSpan(offset + 4), (Half)value.Z);
                        BinaryPrimitives.WriteHalfLittleEndian(linear.AsSpan(offset + 6), (Half)a);
                    }
                });
            }

            if (width == outputWidth && height == outputHeight) return linear;
            using var colorSpace = SKColorSpace.CreateSrgbLinear();
            var alpha = Alpha switch
            {
                CaptureAlphaMode.Opaque => SKAlphaType.Opaque,
                CaptureAlphaMode.Premultiplied => SKAlphaType.Premul,
                _ => SKAlphaType.Unpremul,
            };
            using var source = new SKBitmap(new(width, height, SKColorType.RgbaF16, alpha, colorSpace));
            Marshal.Copy(linear, 0, source.GetPixels(), linear.Length);
            using var destination = new SKBitmap(new(outputWidth, outputHeight,
                SKColorType.RgbaF16, alpha, colorSpace));
            if (!source.ScalePixels(destination, new SKSamplingOptions(SKFilterMode.Linear)))
                throw new InvalidDataException(UiText.Get("InvalidFrameBuffer"));
            var resized = new byte[checked(outputWidth * outputHeight * 8)];
            for (var y = 0; y < outputHeight; y++)
            {
                token.ThrowIfCancellationRequested();
                Marshal.Copy(destination.GetPixels() + y * destination.RowBytes, resized,
                    y * outputWidth * 8, outputWidth * 8);
            }

            return resized;
        }

        private static float Hlg(float value)
        {
            return value <= .5f
                ? value * value / 3
                : (MathF.Exp((value - .55991073f) / .17883277f) + .28466892f) / 12;
        }

        private static float[] Lookup(Func<float, float> convert)
        {
            var values = new float[65536];
            for (var index = 0; index < values.Length; index++) values[index] = convert(index / 65535f);
            return values;
        }
    }
}

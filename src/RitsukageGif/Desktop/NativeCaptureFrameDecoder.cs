using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;

namespace RitsukageGif.Desktop
{
    internal static class NativeCaptureFrameDecoder
    {
        private static readonly Lazy<float[]> Pq = new(() => Lookup(PqValue));
        private static readonly Lazy<float[]> Hlg = new(() => Lookup(HlgValue));

        private static readonly Lazy<float[]> Srgb = new(() => Lookup(value => value <= .04045f
            ? value / 12.92f
            : MathF.Pow((value + .055f) / 1.055f, 2.4f)));

        private static readonly Lazy<float[]> Gamma22 = new(() => Lookup(value => MathF.Pow(value, 2.2f)));
        private static readonly Lazy<float[]> Gamma28 = new(() => Lookup(value => MathF.Pow(value, 2.8f)));
        private static readonly Lazy<float[]> Adobe = new(() => Lookup(value => MathF.Pow(value, 2.19921875f)));

        private static readonly Lazy<float[]> Bt709 = new(() => Lookup(value => value < .081f
            ? value / 4.5f
            : MathF.Pow((value + .099f) / 1.099f, 1 / .45f)));

        public static CapturedFrameSet Decode(NativeCapturePacket packet, CancellationToken token)
        {
            var metadata = packet.Metadata;
            CapturedFrame? hdr = null;
            if (metadata.Hdr is { } high)
            {
                var color = new FrameColorInfo(high.Color.Hdr, high.Color.SdrWhiteNits);
                var alpha = Alpha(high.Alpha);
                if (high is
                    {
                        Format: "rgba16f", Color: { Transfer: "linear", Primaries: "bt709", LinearReferenceNits: 80f },
                    })
                {
                    hdr = new(high.Width, high.Height, high.Stride, CapturePixelFormat.Rgba16FloatScRgb,
                        packet.Hdr, color, alpha, (long)packet.Sequence,
                        [new(new(0, 0, high.Width, high.Height), color)]);
                }
                else
                {
                    var pixels = new byte[checked(high.Width * high.Height * 8)];
                    Convert(high, packet.Hdr, pixels, token);
                    hdr = new(high.Width, high.Height, high.Width * 8, CapturePixelFormat.Rgba16FloatScRgb,
                        pixels, color, CaptureAlphaMode.Premultiplied, (long)packet.Sequence,
                        [new(new(0, 0, high.Width, high.Height), color)]);
                }
            }

            CapturedFrame sdr;
            if (metadata.Sdr is { } low)
            {
                var color = new FrameColorInfo(low.Color.Hdr, low.Color.SdrWhiteNits);
                sdr = new(low.Width, low.Height, low.Stride, CapturePixelFormat.Bgra8Srgb, packet.Sdr,
                    color, Alpha(low.Alpha), (long)packet.Sequence,
                    [new(new(0, 0, low.Width, low.Height), color)]);
            }
            else
            {
                sdr = HdrToneMapping.Map(hdr ?? throw new InvalidDataException("Native capture has no image plane."),
                    token: token);
            }

            return new(sdr, hdr, Stopwatch.GetTimestamp());
        }

        private static void Convert(NativeCapturePlane plane, ReadOnlyMemory<byte> input, byte[] output,
            CancellationToken token)
        {
            var transfer = plane.Color.Transfer;
            var primaries = plane.Color.Primaries;
            if (transfer is not ("linear" or "pq" or "hlg" or "srgb" or "bt709" or "gamma22" or "gamma28"
                    or "adobergb") ||
                primaries is not ("bt709" or "bt2020" or "display-p3"))
                throw new NotSupportedException("The native HDR color encoding is unsupported.");
            var lookup = transfer switch
            {
                "pq" => Pq.Value,
                "hlg" => Hlg.Value,
                "srgb" => Srgb.Value,
                "bt709" => Bt709.Value,
                "gamma22" => Gamma22.Value,
                "gamma28" => Gamma28.Value,
                "adobergb" => Adobe.Value,
                _ => null,
            };
            Parallel.For(0, plane.Height, new() { CancellationToken = token }, y =>
            {
                var source = input.Span.Slice(y * plane.Stride, plane.Width * 8);
                var destination = output.AsSpan(y * plane.Width * 8, plane.Width * 8);
                for (var x = 0; x < plane.Width; x++)
                {
                    var offset = x * 8;
                    var alpha = plane.Alpha == "opaque" ? 1 : Component(source, offset + 6, plane.Format);
                    alpha = float.IsFinite(alpha) ? Math.Clamp(alpha, 0, 1) : 0;
                    var vector = new Vector3(Component(source, offset, plane.Format),
                        Component(source, offset + 2, plane.Format), Component(source, offset + 4, plane.Format));
                    if (plane.Alpha == "premultiplied") vector = alpha > 0 ? vector / alpha : Vector3.Zero;
                    if (lookup is not null)
                    {
                        vector = new(Sample(lookup, vector.X), Sample(lookup, vector.Y), Sample(lookup, vector.Z));
                        if (transfer == "hlg")
                        {
                            var luminance = primaries switch
                            {
                                "bt2020" => Vector3.Dot(vector, new(.2627f, .678f, .0593f)),
                                "display-p3" => Vector3.Dot(vector, new(.2289746f, .6917385f, .0792869f)),
                                _ => Vector3.Dot(vector, new(.2126f, .7152f, .0722f)),
                            };
                            var gamma = 1.2f + .42f * MathF.Log10(plane.Color.PeakNits / 1000);
                            vector *= plane.Color.PeakNits / 80 * MathF.Pow(Math.Max(0, luminance), gamma - 1);
                        }
                        else if (transfer != "pq")
                        {
                            vector *= plane.Color.LinearReferenceNits / 80;
                        }
                    }
                    else
                    {
                        vector *= plane.Color.LinearReferenceNits / 80;
                    }

                    vector = primaries switch
                    {
                        "bt2020" => new(1.660491f * vector.X - .587641f * vector.Y - .07285f * vector.Z,
                            -.12455f * vector.X + 1.1329f * vector.Y - .00835f * vector.Z,
                            -.018151f * vector.X - .100579f * vector.Y + 1.11873f * vector.Z),
                        "display-p3" => new(1.224745f * vector.X - .224904f * vector.Y,
                            -.042058f * vector.X + 1.042081f * vector.Y,
                            -.019642f * vector.X - .078655f * vector.Y + 1.098537f * vector.Z),
                        _ => vector,
                    };
                    vector *= alpha;
                    Write(destination, offset, vector.X);
                    Write(destination, offset + 2, vector.Y);
                    Write(destination, offset + 4, vector.Z);
                    BinaryPrimitives.WriteHalfLittleEndian(destination[(offset + 6)..], (Half)alpha);
                }
            });
        }

        private static float Component(ReadOnlySpan<byte> source, int offset, string format)
        {
            return format == "rgba16f"
                ? (float)BinaryPrimitives.ReadHalfLittleEndian(source[offset..])
                : BinaryPrimitives.ReadUInt16LittleEndian(source[offset..]) / 65535f;
        }

        private static void Write(Span<byte> destination, int offset, float value)
        {
            BinaryPrimitives.WriteHalfLittleEndian(destination[offset..],
                (Half)(float.IsFinite(value) ? Math.Clamp(value, -65504, 65504) : 0));
        }

        private static float Sample(float[] lookup, float value)
        {
            return lookup[float.IsFinite(value) ? Math.Clamp((int)MathF.Round(value * 65535), 0, 65535) : 0];
        }

        private static CaptureAlphaMode Alpha(string value)
        {
            return value switch
            {
                "opaque" => CaptureAlphaMode.Opaque,
                "straight" => CaptureAlphaMode.Straight,
                "premultiplied" => CaptureAlphaMode.Premultiplied,
                _ => throw new InvalidDataException("Invalid native alpha encoding."),
            };
        }

        private static float[] Lookup(Func<float, float> convert)
        {
            var values = new float[65536];
            for (var index = 0; index < values.Length; index++) values[index] = convert(index / 65535f);
            return values;
        }

        private static float PqValue(float encoded)
        {
            var power = MathF.Pow(encoded, 1 / 78.84375f);
            return MathF.Pow(Math.Max(0, power - .8359375f) / (18.8515625f - 18.6875f * power),
                1 / .1593017578125f) * 125;
        }

        private static float HlgValue(float encoded)
        {
            return encoded <= .5f
                ? encoded * encoded / 3
                : (MathF.Exp((encoded - .55991073f) / .17883277f) + .28466892f) / 12;
        }
    }
}

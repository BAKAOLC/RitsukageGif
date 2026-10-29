using System.Buffers.Binary;

namespace RitsukageGif.Core
{
    public static class HdrPngColor
    {
        // Linear scRGB uses Rec.709 primaries and an absolute 80 cd/m² unit.
        // PNG uses straight alpha; RGB is transformed before clipping to the PQ gamut.
        public static void EncodeRow(ReadOnlySpan<byte> rgbaHalf, Span<byte> rgba16BigEndian,
            CaptureAlphaMode alphaMode)
        {
            if (rgbaHalf.Length % 8 != 0 || rgba16BigEndian.Length != rgbaHalf.Length)
                throw new ArgumentException(null, nameof(rgbaHalf));
            for (var i = 0; i < rgbaHalf.Length; i += 8)
            {
                var r = Read(rgbaHalf[i..]);
                var g = Read(rgbaHalf[(i + 2)..]);
                var b = Read(rgbaHalf[(i + 4)..]);
                var alpha = alphaMode == CaptureAlphaMode.Opaque ? 1 : Math.Clamp(Read(rgbaHalf[(i + 6)..]), 0, 1);
                if (alphaMode == CaptureAlphaMode.Premultiplied)
                {
                    var inverse = alpha > 0 ? 1 / alpha : 0;
                    r *= inverse;
                    g *= inverse;
                    b *= inverse;
                }

                // Rec.709 D65 -> Rec.2020 D65; no chromatic adaptation is needed.
                Write(rgba16BigEndian[i..], Pq(.6274038959 * r + .3292830384 * g + .0433130657 * b));
                Write(rgba16BigEndian[(i + 2)..], Pq(.0690972894 * r + .9195403951 * g + .0113623156 * b));
                Write(rgba16BigEndian[(i + 4)..], Pq(.0163914389 * r + .0880133079 * g + .8955952532 * b));
                Write(rgba16BigEndian[(i + 6)..], alpha);
            }
        }

        // Preview decoders provide normalized PQ samples as RGBA FP16. Conversion happens before SDR quantization.
        public static void DecodePreviewRow(ReadOnlySpan<byte> pqHalf, Span<byte> scRgbHalf)
        {
            if (pqHalf.Length % 8 != 0 || scRgbHalf.Length != pqHalf.Length)
                throw new ArgumentException(null, nameof(pqHalf));
            for (var i = 0; i < pqHalf.Length; i += 8)
            {
                var r = Eotf(Read(pqHalf[i..]));
                var g = Eotf(Read(pqHalf[(i + 2)..]));
                var b = Eotf(Read(pqHalf[(i + 4)..]));
                var a = Math.Clamp(Read(pqHalf[(i + 6)..]), 0, 1);
                WriteHalf(scRgbHalf[i..], 1.6604910021 * r - .5876411388 * g - .0728498633 * b);
                WriteHalf(scRgbHalf[(i + 2)..], -.1245504745 * r + 1.1328998971 * g - .0083494226 * b);
                WriteHalf(scRgbHalf[(i + 4)..], -.0181507634 * r - .1005788980 * g + 1.1187296614 * b);
                WriteHalf(scRgbHalf[(i + 6)..], a);
            }
        }

        private static double Eotf(double code)
        {
            var value = Math.Pow(Math.Clamp(code, 0, 1), 32d / 2523);
            return 125 * Math.Pow(Math.Max(value - 3424d / 4096, 0) / (2413d / 128 - 2392d / 128 * value),
                16384d / 2610);
        }

        private static void WriteHalf(Span<byte> output, double value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(output, BitConverter.HalfToUInt16Bits((Half)value));
        }

        private static double Read(ReadOnlySpan<byte> value)
        {
            var sample = (double)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(value));
            return double.IsFinite(sample) ? sample : 0;
        }

        private static double Pq(double linear)
        {
            // ITU-R BT.2100 inverse EOTF, normalized to its absolute 10,000 cd/m² ceiling.
            var value = Math.Pow(Math.Clamp(linear * .008, 0, 1), 2610d / 16384);
            return Math.Pow((3424d / 4096 + 2413d / 128 * value) / (1 + 2392d / 128 * value), 2523d / 32);
        }

        private static void Write(Span<byte> output, double value)
        {
            BinaryPrimitives.WriteUInt16BigEndian(output, (ushort)Math.Clamp(Math.Round(value * 65535), 0, 65535));
        }
    }
}

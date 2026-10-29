using System.Runtime.InteropServices;

namespace RitsukageGif.Core
{
    public static class ScRgbToneMapper
    {
        private static readonly byte[] Transfer = CreateTransfer();
        private static readonly float[] Linear = CreateLinear();

        public static unsafe void ConvertRows(nint source, uint pitch, byte[] destination, int width, int height,
            float whiteLevel, bool hdr, bool straightOutput = false)
        {
            ArgumentNullException.ThrowIfNull(destination);
            if (source == 0) throw new ArgumentException("A source pixel buffer is required.", nameof(source));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            if (pitch < (long)width * 8) throw new ArgumentOutOfRangeException(nameof(pitch));
            if (destination.LongLength < (long)width * height * 4)
                throw new ArgumentException("The destination buffer is too small.", nameof(destination));
            var blocks = Math.Min(Environment.ProcessorCount, Math.Max(1, height / 128));
            Parallel.For(0, blocks, block =>
            {
                var start = height * block / blocks;
                var end = height * (block + 1) / blocks;
                for (var y = start; y < end; y++)
                    Convert(new((byte*)source + y * pitch, width * 4),
                        destination.AsSpan(y * width * 4, width * 4), whiteLevel, hdr, straightOutput: straightOutput);
            });
        }

        internal static void Convert(ReadOnlySpan<Half> source, Span<byte> destination, float whiteLevel, bool hdr,
            HdrToneMapMode mode = HdrToneMapMode.HighlightCompression,
            CaptureAlphaMode alphaMode = CaptureAlphaMode.Premultiplied, bool straightOutput = false)
        {
            if (source.Length % 4 != 0) throw new ArgumentException("Expected RGBA components.", nameof(source));
            if (destination.Length != source.Length)
                throw new ArgumentException("Source and destination component counts must match.", nameof(destination));
            if (!float.IsFinite(whiteLevel) || whiteLevel <= 0)
                throw new ArgumentOutOfRangeException(nameof(whiteLevel));
            var exposure = 1 / whiteLevel;
            var bits = MemoryMarshal.Cast<Half, ushort>(source);
            for (var i = 0; i < source.Length; i += 4)
            {
                var alpha = alphaMode == CaptureAlphaMode.Opaque ? 1 : Math.Clamp(Linear[bits[i + 3]], 0, 1);
                var unpremultiply = alphaMode == CaptureAlphaMode.Premultiplied && alpha > 0 ? 1 / alpha : 1;
                var r = Linear[bits[i]] * exposure * unpremultiply;
                var g = Linear[bits[i + 1]] * exposure * unpremultiply;
                var b = Linear[bits[i + 2]] * exposure * unpremultiply;
                var minimum = Math.Min(r, Math.Min(g, b));
                if (minimum < 0)
                {
                    var luminance = Math.Max(0, 0.2126f * r + 0.7152f * g + 0.0722f * b);
                    var saturation = luminance / (luminance - minimum);
                    r = luminance + (r - luminance) * saturation;
                    g = luminance + (g - luminance) * saturation;
                    b = luminance + (b - luminance) * saturation;
                }

                var maximum = Math.Max(r, Math.Max(g, b));
                if (hdr && mode == HdrToneMapMode.HighlightCompression && maximum > 0.75f)
                {
                    var mapped = 1 - 0.0625f / (maximum - 0.5f);
                    var scale = mapped / maximum;
                    r *= scale;
                    g *= scale;
                    b *= scale;
                }

                var premultiply = alphaMode == CaptureAlphaMode.Premultiplied && !straightOutput ? alpha : 1;
                destination[i] = (byte)(Encode(b) * premultiply + 0.5f);
                destination[i + 1] = (byte)(Encode(g) * premultiply + 0.5f);
                destination[i + 2] = (byte)(Encode(r) * premultiply + 0.5f);
                destination[i + 3] = (byte)Math.Clamp((int)(alpha * 255 + 0.5f), 0, 255);
            }
        }

        private static float[] CreateLinear()
        {
            var table = new float[65536];
            for (var i = 0; i < table.Length; i++) table[i] = Sanitize((float)BitConverter.UInt16BitsToHalf((ushort)i));
            return table;
        }

        private static float Sanitize(float value)
        {
            return float.IsFinite(value) ? value : 0;
        }

        private static byte Encode(float value)
        {
            value = Math.Clamp(value, 0, 1);
            return Transfer[(int)(value * 65535 + 0.5f)];
        }

        private static byte[] CreateTransfer()
        {
            var table = new byte[65536];
            for (var i = 0; i < table.Length; i++)
            {
                var value = i / 65535f;
                var encoded = value <= 0.0031308f ? 12.92f * value : 1.055f * MathF.Pow(value, 1 / 2.4f) - 0.055f;
                table[i] = (byte)Math.Clamp((int)(encoded * 255 + 0.5f), 0, 255);
            }

            return table;
        }
    }
}

using System.Numerics;

namespace RitsukageGif.Core
{
    public static partial class ImageInpainting
    {
        public static void RestoreBgra(byte[] pixels, int width, int height, ushort[] coverage, int radius = 4,
            CancellationToken token = default)
        {
            RestoreBytes(pixels, width, height, coverage, radius, true, token);
        }

        public static void RestoreRgba(byte[] pixels, int width, int height, ushort[] coverage, int radius = 4,
            CancellationToken token = default)
        {
            RestoreBytes(pixels, width, height, coverage, radius, false, token);
        }

        private static void RestoreBytes(byte[] pixels, int width, int height, ushort[] coverage, int radius,
            bool bgra, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(pixels);
            if (width <= 0 || height <= 0 || (long)width * height * 4 != pixels.Length)
                throw new ArgumentException("The RGBA image dimensions must match the pixel buffer.");
            var region = MaskRegion(coverage, width, height, radius, token);
            if (region.IsEmpty) return;
            if (region.Width != width || region.Height != height)
            {
                var cropped = new byte[checked(region.Width * region.Height * 4)];
                for (var y = 0; y < region.Height; y++)
                    Buffer.BlockCopy(pixels, ((region.Y + y) * width + region.X) * 4,
                        cropped, y * region.Width * 4, region.Width * 4);
                RestoreBytes(cropped, region.Width, region.Height, CropCoverage(coverage, width, region), radius, bgra,
                    token);
                token.ThrowIfCancellationRequested();
                for (var y = 0; y < region.Height; y++)
                    Buffer.BlockCopy(cropped, y * region.Width * 4, pixels,
                        ((region.Y + y) * width + region.X) * 4, region.Width * 4);
                return;
            }

            var samples = new Vector4[checked(width * height)];
            var red = bgra ? 2 : 0;
            var blue = bgra ? 0 : 2;
            for (var index = 0; index < samples.Length; index++)
            {
                if ((index & 4095) == 0) token.ThrowIfCancellationRequested();
                var offset = index * 4;
                var alpha = pixels[offset + 3] / 255f;
                samples[index] = new(Decode(pixels[offset + red] / 255f) * alpha,
                    Decode(pixels[offset + 1] / 255f) * alpha,
                    Decode(pixels[offset + blue] / 255f) * alpha, alpha);
            }

            Restore(samples, width, height, coverage, radius, token);
            Span<byte> restored = stackalloc byte[4];
            for (var index = 0; index < samples.Length; index++)
            {
                if ((index & 4095) == 0) token.ThrowIfCancellationRequested();
                var amount = BitOperations.PopCount(coverage[index]);
                if (amount == 0) continue;
                var offset = index * 4;
                var value = samples[index];
                var alpha = value.W;
                restored[red] = EncodeByte(alpha > 0 ? value.X / alpha : 0);
                restored[1] = EncodeByte(alpha > 0 ? value.Y / alpha : 0);
                restored[blue] = EncodeByte(alpha > 0 ? value.Z / alpha : 0);
                restored[3] = (byte)Math.Clamp(MathF.Round(alpha * 255), 0, 255);
                if (amount == 16)
                {
                    restored.CopyTo(pixels.AsSpan(offset, 4));
                    continue;
                }

                var front = restored[3] * amount;
                var back = pixels[offset + 3] * (16 - amount);
                var weight = front + back;
                for (var channel = 0; channel < 3; channel++)
                    pixels[offset + channel] = weight > 0
                        ? (byte)((restored[channel] * front + pixels[offset + channel] * back + weight / 2) / weight)
                        : (byte)0;
                pixels[offset + 3] = (byte)((weight + 8) / 16);
            }
        }

        private static byte EncodeByte(float value)
        {
            var encoded = value <= .0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1 / 2.4f) - .055f;
            return (byte)Math.Clamp(MathF.Round(encoded * 255), 0, 255);
        }

        private static float Decode(float value)
        {
            return value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
        }
    }
}

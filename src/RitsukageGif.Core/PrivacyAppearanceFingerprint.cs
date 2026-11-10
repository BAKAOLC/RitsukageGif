namespace RitsukageGif.Core
{
    internal static class PrivacyAppearanceFingerprint
    {
        private static readonly double[] Cosines = CreateCosines();

        internal static ulong Create(ReadOnlySpan<byte> pixels, int stride, PixelRect area,
            CancellationToken token)
        {
            Span<double> sample = stackalloc double[32 * 32];
            double sum = 0;
            for (var y = 0; y < 32; y++)
            {
                token.ThrowIfCancellationRequested();
                var sy = Math.Clamp((y + .5) * area.Height / 32 - .5, 0, area.Height - 1);
                var top = (int)sy;
                var bottom = Math.Min(area.Height - 1, top + 1);
                for (var x = 0; x < 32; x++)
                {
                    var sx = Math.Clamp((x + .5) * area.Width / 32 - .5, 0, area.Width - 1);
                    var left = (int)sx;
                    var right = Math.Min(area.Width - 1, left + 1);
                    var a = Gray(pixels, (area.Y + top) * stride + (area.X + left) * 4);
                    var b = Gray(pixels, (area.Y + top) * stride + (area.X + right) * 4);
                    var c = Gray(pixels, (area.Y + bottom) * stride + (area.X + left) * 4);
                    var d = Gray(pixels, (area.Y + bottom) * stride + (area.X + right) * 4);
                    var upper = a + (b - a) * (sx - left);
                    var lower = c + (d - c) * (sx - left);
                    var value = upper + (lower - upper) * (sy - top);
                    sample[y * 32 + x] = value;
                    sum += value;
                }
            }

            var average = sum / sample.Length;
            Span<double> rows = stackalloc double[32 * 8];
            for (var y = 0; y < 32; y++)
            for (var u = 0; u < 8; u++)
            {
                double value = 0;
                for (var x = 0; x < 32; x++)
                    value += (sample[y * 32 + x] - average) * Cosines[u * 32 + x];
                rows[y * 8 + u] = value;
            }

            Span<double> coefficients = stackalloc double[64];
            for (var v = 0; v < 8; v++)
            for (var u = 0; u < 8; u++)
            {
                double value = 0;
                for (var y = 0; y < 32; y++) value += rows[y * 8 + u] * Cosines[v * 32 + y];
                if (u == 0) value *= Math.Sqrt(.5);
                if (v == 0) value *= Math.Sqrt(.5);
                coefficients[v * 8 + u] = value;
            }

            Span<double> ordered = stackalloc double[63];
            coefficients[1..].CopyTo(ordered);
            ordered.Sort();
            var median = ordered[ordered.Length / 2];
            ulong signature = 0;
            for (var index = 1; index < 64; index++)
                if (coefficients[index] > median)
                    signature |= 1UL << index;
            return signature;
        }

        private static double Gray(ReadOnlySpan<byte> pixels, int offset)
        {
            var gray = pixels[offset + 2] * .299 + pixels[offset + 1] * .587 + pixels[offset] * .114;
            var alpha = pixels[offset + 3] / 255d;
            return gray * alpha + 255 * (1 - alpha);
        }

        private static double[] CreateCosines()
        {
            var values = new double[8 * 32];
            for (var frequency = 0; frequency < 8; frequency++)
            for (var position = 0; position < 32; position++)
                values[frequency * 32 + position] =
                    Math.Cos(Math.PI * (2 * position + 1) * frequency / 64);
            return values;
        }
    }
}

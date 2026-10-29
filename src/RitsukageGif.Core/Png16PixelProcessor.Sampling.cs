namespace RitsukageGif.Core
{
    public static partial class Png16PixelProcessor
    {
        public static ushort[] SampleScaled(byte[] original, int width, int height, PixelRect destination,
            double originX, double originY, double outputWidth, double outputHeight, double opacity,
            CancellationToken token = default)
        {
            Validate(original, width, height);
            if (destination.IsEmpty || !double.IsFinite(outputWidth) || !double.IsFinite(outputHeight) ||
                outputWidth <= 0 || outputHeight <= 0 || !double.IsFinite(originX) || !double.IsFinite(originY) ||
                !double.IsFinite(opacity) || opacity is < 0 or > 1 ||
                (long)destination.Width * destination.Height > 1024L * 1024)
                throw new ArgumentException("Invalid resampling region.", nameof(destination));
            var output = new ushort[checked(destination.Width * destination.Height * 4)];
            Span<double> sum = stackalloc double[4];
            for (var y = 0; y < destination.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                var pixelY = destination.Y + y;
                var sy = (pixelY + .5 - originY) * height / outputHeight - .5;
                var top = (int)Math.Floor(sy);
                var fractionY = sy - top;
                for (var x = 0; x < destination.Width; x++)
                {
                    var pixelX = destination.X + x;
                    var coverage = Math.Clamp(Math.Min(pixelX + 1d, originX + outputWidth) - Math.Max(pixelX, originX),
                                       0, 1) *
                                   Math.Clamp(Math.Min(pixelY + 1d, originY + outputHeight) - Math.Max(pixelY, originY),
                                       0, 1);
                    if (coverage == 0) continue;
                    var sx = (pixelX + .5 - originX) * width / outputWidth - .5;
                    var left = (int)Math.Floor(sx);
                    var fractionX = sx - left;
                    sum.Clear();
                    for (var row = 0; row < 2; row++)
                    for (var column = 0; column < 2; column++)
                    {
                        var sampleX = Math.Clamp(left + column, 0, width - 1);
                        var sampleY = Math.Clamp(top + row, 0, height - 1);
                        var weight = (row == 0 ? 1 - fractionY : fractionY) * (column == 0 ? 1 - fractionX : fractionX);
                        var pixel = original.AsSpan((sampleY * width + sampleX) * 8, 8);
                        var alpha = Read(pixel, 3) * weight;
                        for (var channel = 0; channel < 3; channel++) sum[channel] += Read(pixel, channel) * alpha;
                        sum[3] += alpha;
                    }

                    var result = output.AsSpan((y * destination.Width + x) * 4, 4);
                    for (var channel = 0; channel < 3; channel++)
                        result[channel] = sum[3] > 0
                            ? (ushort)Math.Clamp(Math.Round(sum[channel] / sum[3]), 0, 65535)
                            : (ushort)0;
                    result[3] = (ushort)Math.Clamp(Math.Round(sum[3] * opacity * coverage), 0, 65535);
                }
            }

            return output;
        }
    }
}

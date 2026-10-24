namespace RitsukageGif.Core
{
    public static unsafe partial class PixelEffectProcessor
    {
        private static void BlurVerticalStrip(nint buffer, nint pixels, int stride, PixelRect area, PixelEffect effect,
            bool premultiplied, int left, CancellationToken token)
        {
            var width = Math.Min(64, area.Width - left);
            var sums = stackalloc long[64 * 4];
            new Span<long>(sums, width * 4).Clear();
            var source = (byte*)buffer + left * 4L;
            var bottom = Math.Min(area.Height - 1, effect.Radius);
            for (var y = 0; y <= bottom; y++)
            for (var x = 0; x < width; x++)
            {
                var sum = sums + x * 4;
                Accumulate(source + (long)y * area.Width * 4 + x * 4, true, 1,
                    ref sum[0], ref sum[1], ref sum[2], ref sum[3]);
            }

            for (var y = 0; y < area.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                var count = Math.Min(area.Height - 1, y + effect.Radius) - Math.Max(0, y - effect.Radius) + 1;
                var target = (byte*)pixels + (long)(area.Y + y) * stride + (area.X + left) * 4L;
                var remove = y - effect.Radius;
                var add = y + effect.Radius + 1;
                for (var x = 0; x < width; x++, target += 4)
                {
                    var sum = sums + x * 4;
                    var factor = sum[3] > 0 ? 1.0 / sum[3] : 0;
                    Write(target, sum[0] * factor, sum[1] * factor, sum[2] * factor, (double)sum[3] / count,
                        effect.Amount, premultiplied);
                    if (remove >= 0)
                        Accumulate(source + (long)remove * area.Width * 4 + x * 4, true, -1,
                            ref sum[0], ref sum[1], ref sum[2], ref sum[3]);
                    if (add < area.Height)
                        Accumulate(source + (long)add * area.Width * 4 + x * 4, true, 1,
                            ref sum[0], ref sum[1], ref sum[2], ref sum[3]);
                }
            }
        }
    }
}

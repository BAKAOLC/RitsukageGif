using System.Buffers;
using System.Buffers.Binary;

namespace RitsukageGif.Core
{
    public static unsafe partial class PixelEffectProcessor
    {
        private static void Mosaic(nint pixels, int stride, PixelRect area, PixelEffect effect,
            ParallelOptions parallel, bool premultiplied)
        {
            var size = effect.BlockSize;
            Rows((area.Height + size - 1) / size, (long)area.Width * size, parallel, blockRow =>
            {
                var top = area.Y + blockRow * size;
                var bottom = Math.Min(top + size, area.Bottom);
                for (var left = area.X; left < area.Right; left += size)
                {
                    parallel.CancellationToken.ThrowIfCancellationRequested();
                    var right = Math.Min(left + size, area.Right);
                    long b = 0, g = 0, r = 0, a = 0;
                    for (var y = top; y < bottom; y++)
                    {
                        if ((y & 63) == 0) parallel.CancellationToken.ThrowIfCancellationRequested();
                        var pixel = (byte*)pixels + (long)y * stride + left * 4L;
                        for (var x = left; x < right; x++, pixel += 4)
                            Accumulate(pixel, premultiplied, 1, ref b, ref g, ref r, ref a);
                    }

                    var factor = a > 0 ? 1.0 / a : 0;
                    var count = (right - left) * (bottom - top);
                    // Opaque full-strength blocks have identical output pixels; preserve the same rounding
                    // once, then fill each block row without repeating the blend for every pixel.
                    // ReSharper disable once CompareOfFloatsByEqualityOperator
                    if (a == 255L * count && effect.Amount == 1)
                    {
                        var color = Byte(b * factor) | ((uint)Byte(g * factor) << 8) |
                                    ((uint)Byte(r * factor) << 16) | 0xff000000;
                        if (!BitConverter.IsLittleEndian) color = BinaryPrimitives.ReverseEndianness(color);
                        for (var y = top; y < bottom; y++)
                        {
                            if ((y & 63) == 0) parallel.CancellationToken.ThrowIfCancellationRequested();
                            var pixel = (byte*)pixels + (long)y * stride + left * 4L;
                            new Span<uint>(pixel, right - left).Fill(color);
                        }

                        continue;
                    }

                    var alpha = (double)a / count;
                    for (var y = top; y < bottom; y++)
                    {
                        if ((y & 63) == 0) parallel.CancellationToken.ThrowIfCancellationRequested();
                        var pixel = (byte*)pixels + (long)y * stride + left * 4L;
                        for (var x = left; x < right; x++, pixel += 4)
                            Write(pixel, b * factor, g * factor, r * factor, alpha, effect.Amount, premultiplied);
                    }
                }
            });
        }

        private static void Blur(nint pixels, int stride, PixelRect area, PixelEffect effect,
            ParallelOptions parallel, bool premultiplied)
        {
            var temporary = ArrayPool<byte>.Shared.Rent(checked(area.Width * area.Height * 4));
            try
            {
                fixed (byte* temporaryPointer = temporary)
                {
                    var buffer = (nint)temporaryPointer;
                    // Horizontal sliding sums write premultiplied intermediate pixels; no neighboring source
                    // pixels are overwritten until this pass has completed for every row.
                    Rows(area.Height, area.Width, parallel, row =>
                    {
                        var source = (byte*)pixels + (long)(area.Y + row) * stride + area.X * 4L;
                        var target = (byte*)buffer + (long)row * area.Width * 4;
                        long b = 0, g = 0, r = 0, a = 0;
                        var right = Math.Min(area.Width - 1, effect.Radius);
                        for (var x = 0; x <= right; x++)
                            Accumulate(source + x * 4L, premultiplied, 1, ref b, ref g, ref r, ref a);
                        for (var x = 0; x < area.Width; x++)
                        {
                            if ((x & 4095) == 0) parallel.CancellationToken.ThrowIfCancellationRequested();
                            var count = Math.Min(area.Width - 1, x + effect.Radius) - Math.Max(0, x - effect.Radius) +
                                        1;
                            target[x * 4] = Byte((double)b / (count * 255));
                            target[x * 4 + 1] = Byte((double)g / (count * 255));
                            target[x * 4 + 2] = Byte((double)r / (count * 255));
                            target[x * 4 + 3] = Byte((double)a / count);
                            var remove = x - effect.Radius;
                            var add = x + effect.Radius + 1;
                            if (remove >= 0)
                                Accumulate(source + remove * 4L, premultiplied, -1, ref b, ref g, ref r, ref a);
                            if (add < area.Width)
                                Accumulate(source + add * 4L, premultiplied, 1, ref b, ref g, ref r, ref a);
                        }
                    });
                    if ((long)area.Width * area.Height >= 4L * 1024 * 1024 &&
                        area.Width / 64 >= parallel.MaxDegreeOfParallelism)
                        // Process neighboring columns together so each intermediate and output row stays local
                        // in cache. Strips own disjoint output pixels and retain the original strength blending.
                        Rows((area.Width + 63) / 64, (long)area.Height * 64, parallel, strip =>
                            BlurVerticalStrip(buffer, pixels, stride, area, effect, premultiplied, strip * 64,
                                parallel.CancellationToken));
                    else
                        // Columns are independent; the original buffer is retained for strength blending.
                        Rows(area.Width, area.Height, parallel, column =>
                        {
                            var source = (byte*)buffer + column * 4L;
                            long b = 0, g = 0, r = 0, a = 0;
                            var bottom = Math.Min(area.Height - 1, effect.Radius);
                            for (var y = 0; y <= bottom; y++)
                                Accumulate(source + (long)y * area.Width * 4, true, 1, ref b, ref g, ref r, ref a);
                            for (var y = 0; y < area.Height; y++)
                            {
                                if ((y & 63) == 0) parallel.CancellationToken.ThrowIfCancellationRequested();
                                var count = Math.Min(area.Height - 1, y + effect.Radius) -
                                            Math.Max(0, y - effect.Radius) +
                                            1;
                                var factor = a > 0 ? 1.0 / a : 0;
                                var target = (byte*)pixels + (long)(area.Y + y) * stride + (area.X + column) * 4L;
                                Write(target, b * factor, g * factor, r * factor, (double)a / count, effect.Amount,
                                    premultiplied);
                                var remove = y - effect.Radius;
                                var add = y + effect.Radius + 1;
                                if (remove >= 0)
                                    Accumulate(source + (long)remove * area.Width * 4, true, -1, ref b, ref g, ref r,
                                        ref a);
                                if (add < area.Height)
                                    Accumulate(source + (long)add * area.Width * 4, true, 1, ref b, ref g, ref r,
                                        ref a);
                            }
                        });
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(temporary);
            }
        }

        private static void Accumulate(byte* pixel, bool premultiplied, int sign,
            ref long b, ref long g, ref long r, ref long a)
        {
            var multiplier = (premultiplied ? 255 : pixel[3]) * sign;
            b += pixel[0] * multiplier;
            g += pixel[1] * multiplier;
            r += pixel[2] * multiplier;
            a += pixel[3] * sign;
        }
    }
}

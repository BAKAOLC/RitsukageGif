using System.Numerics;

namespace RitsukageGif.Core
{
    public static partial class HdrPixelEffectProcessor
    {
        private static void Mosaic(byte[] pixels, int stride, CaptureAlphaMode mode, PixelRect area,
            PixelEffect effect, CancellationToken token)
        {
            for (var top = area.Y; top < area.Bottom; top += effect.BlockSize)
            for (var left = area.X; left < area.Right; left += effect.BlockSize)
            {
                token.ThrowIfCancellationRequested();
                var right = Math.Min(area.Right, left + effect.BlockSize);
                var bottom = Math.Min(area.Bottom, top + effect.BlockSize);
                var sum = Vector4.Zero;
                for (var y = top; y < bottom; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var x = left; x < right; x++) sum += Read(pixels.AsSpan(y * stride + x * 8, 8), mode);
                }

                var average = sum / ((right - left) * (bottom - top));
                for (var y = top; y < bottom; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var x = left; x < right; x++)
                    {
                        var target = pixels.AsSpan(y * stride + x * 8, 8);
                        Write(target, Vector4.Lerp(Read(target, mode), average, (float)effect.Amount), mode);
                    }
                }
            }
        }

        private static void Blur(byte[] pixels, int stride, CaptureAlphaMode mode, PixelRect area,
            PixelEffect effect, CancellationToken token)
        {
            // Keep a compact FP16 region snapshot; only the column accumulator uses FP32.
            // This avoids a full-frame floating-point expansion and keeps work O(width * height).
            var rowBytes = checked(area.Width * 8);
            var source = new byte[checked(rowBytes * area.Height)];
            for (var y = 0; y < area.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                pixels.AsSpan((area.Y + y) * stride + area.X * 8, rowBytes)
                    .CopyTo(source.AsSpan(y * rowBytes, rowBytes));
            }

            var columns = new Vector4[area.Width];
            for (var y = 0; y <= Math.Min(effect.Radius, area.Height - 1); y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < area.Width; x++) columns[x] += Read(source.AsSpan(y * rowBytes + x * 8, 8), mode);
            }

            for (var y = 0; y < area.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                var rowCount = Math.Min(area.Height - 1, y + effect.Radius) - Math.Max(0, y - effect.Radius) + 1;
                var sum = Vector4.Zero;
                for (var x = 0; x <= Math.Min(effect.Radius, area.Width - 1); x++) sum += columns[x];
                for (var x = 0; x < area.Width; x++)
                {
                    var columnCount = Math.Min(area.Width - 1, x + effect.Radius) - Math.Max(0, x - effect.Radius) + 1;
                    var target = pixels.AsSpan((area.Y + y) * stride + (area.X + x) * 8, 8);
                    Write(target,
                        Vector4.Lerp(Read(target, mode), sum / (rowCount * columnCount), (float)effect.Amount), mode);
                    if (x - effect.Radius >= 0) sum -= columns[x - effect.Radius];
                    if (x + effect.Radius + 1 < area.Width) sum += columns[x + effect.Radius + 1];
                }

                var remove = y - effect.Radius;
                var add = y + effect.Radius + 1;
                for (var x = 0; x < area.Width; x++)
                {
                    if (remove >= 0) columns[x] -= Read(source.AsSpan(remove * rowBytes + x * 8, 8), mode);
                    if (add < area.Height) columns[x] += Read(source.AsSpan(add * rowBytes + x * 8, 8), mode);
                }
            }
        }
    }
}

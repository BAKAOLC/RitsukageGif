namespace RitsukageGif.Core
{
    public static partial class Png16PixelProcessor
    {
        public static void Mosaic(byte[] target, byte[] original, int width, int height, PixelRect region,
            int blockSize, CancellationToken token = default)
        {
            Validate(target, width, height);
            Validate(original, width, height);
            if (blockSize is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(blockSize));
            var area = Clip(region, width, height);
            Span<ulong> sum = stackalloc ulong[4];
            for (var top = area.Y; top < area.Bottom; top += blockSize)
            for (var left = area.X; left < area.Right; left += blockSize)
            {
                sum.Clear();
                var right = Math.Min(area.Right, left + blockSize);
                var bottom = Math.Min(area.Bottom, top + blockSize);
                for (var y = top; y < bottom; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var x = left; x < right; x++)
                    {
                        var pixel = original.AsSpan((y * width + x) * 8, 8);
                        var alpha = (ulong)Read(pixel, 3);
                        for (var channel = 0; channel < 3; channel++) sum[channel] += Read(pixel, channel) * alpha;
                        sum[3] += alpha;
                    }
                }

                var count = (ulong)(right - left) * (ulong)(bottom - top);
                for (var y = top; y < bottom; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var x = left; x < right; x++)
                    {
                        var pixel = target.AsSpan((y * width + x) * 8, 8);
                        for (var channel = 0; channel < 3; channel++)
                            Write(pixel, channel,
                                sum[3] > 0 ? (ushort)((sum[channel] + sum[3] / 2) / sum[3]) : (ushort)0);
                        Write(pixel, 3, (ushort)((sum[3] + count / 2) / count));
                    }
                }
            }
        }

        public static void Blur(byte[] target, byte[] original, int width, int height, PixelRect region,
            int radius, CancellationToken token = default)
        {
            Validate(target, width, height);
            Validate(original, width, height);
            if (ReferenceEquals(target, original))
                throw new ArgumentException("Blur requires an immutable source.", nameof(original));
            if (radius is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(radius));
            var area = Clip(region, width, height);
            const int tileSize = 512;
            Span<ulong> sum = stackalloc ulong[4];
            for (var top = area.Y; top < area.Bottom; top += tileSize)
            for (var left = area.X; left < area.Right; left += tileSize)
            {
                var right = Math.Min(area.Right, left + tileSize);
                var bottom = Math.Min(area.Bottom, top + tileSize);
                var sampleLeft = Math.Max(0, left - radius);
                var sampleRight = Math.Min(width, right + radius);
                var columns = new ulong[(sampleRight - sampleLeft) * 4];
                for (var y = Math.Max(0, top - radius); y <= Math.Min(height - 1, top + radius); y++)
                    AdjustColumns(original, width, y, sampleLeft, columns, true, token);
                for (var y = top; y < bottom; y++)
                {
                    token.ThrowIfCancellationRequested();
                    sum.Clear();
                    for (var x = Math.Max(0, left - radius); x <= Math.Min(width - 1, left + radius); x++)
                    for (var channel = 0; channel < 4; channel++)
                        sum[channel] += columns[(x - sampleLeft) * 4 + channel];
                    var rowCount = Math.Min(height - 1, y + radius) - Math.Max(0, y - radius) + 1;
                    for (var x = left; x < right; x++)
                    {
                        var columnCount = Math.Min(width - 1, x + radius) - Math.Max(0, x - radius) + 1;
                        var count = (ulong)rowCount * (ulong)columnCount;
                        var pixel = target.AsSpan((y * width + x) * 8, 8);
                        for (var channel = 0; channel < 3; channel++)
                            Write(pixel, channel,
                                sum[3] > 0 ? (ushort)((sum[channel] + sum[3] / 2) / sum[3]) : (ushort)0);
                        Write(pixel, 3, (ushort)((sum[3] + count / 2) / count));
                        var remove = x - radius;
                        var add = x + radius + 1;
                        for (var channel = 0; channel < 4; channel++)
                        {
                            if (remove >= sampleLeft) sum[channel] -= columns[(remove - sampleLeft) * 4 + channel];
                            if (add < sampleRight) sum[channel] += columns[(add - sampleLeft) * 4 + channel];
                        }
                    }

                    if (y - radius >= 0) AdjustColumns(original, width, y - radius, sampleLeft, columns, false, token);
                    if (y + radius + 1 < height)
                        AdjustColumns(original, width, y + radius + 1, sampleLeft, columns, true, token);
                }
            }
        }

        public static void Magnify(byte[] target, byte[] original, int width, int height, PixelRect region,
            double centerX, double centerY, double scale, CancellationToken token = default)
        {
            Validate(target, width, height);
            Validate(original, width, height);
            if (ReferenceEquals(target, original))
                throw new ArgumentException("Magnification requires an immutable source.", nameof(original));
            if (!double.IsFinite(scale) || scale < 1 || !double.IsFinite(centerX) || !double.IsFinite(centerY))
                throw new ArgumentOutOfRangeException(nameof(scale));
            var area = Clip(region, width, height);
            for (var y = area.Y; y < area.Bottom; y++)
            {
                token.ThrowIfCancellationRequested();
                var sy = Math.Clamp((int)Math.Floor(centerY + (y + .5 - centerY) / scale), 0, height - 1);
                for (var x = area.X; x < area.Right; x++)
                {
                    var sx = Math.Clamp((int)Math.Floor(centerX + (x + .5 - centerX) / scale), 0, width - 1);
                    original.AsSpan((sy * width + sx) * 8, 8).CopyTo(target.AsSpan((y * width + x) * 8, 8));
                }
            }
        }

        private static void AdjustColumns(byte[] original, int width, int y, int left, ulong[] columns,
            bool add, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < columns.Length / 4; x++)
            {
                var pixel = original.AsSpan((y * width + left + x) * 8, 8);
                var alpha = (ulong)Read(pixel, 3);
                for (var channel = 0; channel < 4; channel++)
                {
                    var value = channel == 3 ? alpha : Read(pixel, channel) * alpha;
                    if (add) columns[x * 4 + channel] += value;
                    else columns[x * 4 + channel] -= value;
                }
            }
        }
    }
}

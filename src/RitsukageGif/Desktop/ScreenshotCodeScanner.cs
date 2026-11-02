namespace RitsukageGif.Desktop
{
    internal static class ScreenshotCodeScanner
    {
        private const int MaximumPixels = 16 * 1024 * 1024;
        private const int TileSize = 4096;
        private const int TileStep = 3072;

        internal static async Task<IReadOnlyList<BarcodeRecognitionResult>> ScanAsync(PixelRect region,
            IReadOnlyList<(PixelRect Region, byte[] Pixels)> frames, CancellationToken token)
        {
            var service = new BarcodeRecognitionService();
            var results = new List<BarcodeRecognitionResult>();
            if (region is { Width: <= 32768, Height: <= 32768 } &&
                (long)region.Width * region.Height <= MaximumPixels)
            {
                var frame = await Task.Run(() => Read(region, frames, 1, token), token).ConfigureAwait(false);
                return await service.RecognizeAsync(frame, token).ConfigureAwait(false);
            }

            var scale = Math.Max(2, (int)Math.Ceiling(Math.Sqrt((long)region.Width * region.Height / 2097152d)));
            var overview = await Task.Run(() => Read(region, frames, scale, token), token).ConfigureAwait(false);
            Append(await service.RecognizeAsync(overview, token).ConfigureAwait(false), region.X, region.Y, scale);
            for (var y = region.Y; y < region.Bottom; y += TileStep)
            for (var x = region.X; x < region.Right; x += TileStep)
            {
                token.ThrowIfCancellationRequested();
                var tile = new PixelRect(x, y, Math.Min(TileSize, region.Right - x),
                    Math.Min(TileSize, region.Bottom - y));
                var frame = await Task.Run(() => Read(tile, frames, 1, token), token).ConfigureAwait(false);
                Append(await service.RecognizeAsync(frame, token).ConfigureAwait(false), x, y, 1);
            }

            return results;

            void Append(IReadOnlyList<BarcodeRecognitionResult> codes, int x, int y, int factor)
            {
                foreach (var code in codes)
                {
                    var candidate = new BarcodeRecognitionResult(code.Text, code.Format,
                        [.. code.Points.Select(Translate)]) { Outline = [.. code.Outline.Select(Translate)] };
                    if (!results.Any(previous => Same(previous, candidate))) results.Add(candidate);
                }

                return;

                BarcodeRecognitionPoint Translate(BarcodeRecognitionPoint point)
                {
                    return new(point.X * factor + x - region.X, point.Y * factor + y - region.Y);
                }
            }
        }

        private static bool Same(BarcodeRecognitionResult left, BarcodeRecognitionResult right)
        {
            if (left.Format != right.Format || left.Text != right.Text) return false;
            var a = left.Outline.Count > 0 ? left.Outline : left.Points;
            var b = right.Outline.Count > 0 ? right.Outline : right.Points;
            if (a.Count == 0 || b.Count == 0) return false;
            var ax = a.Min(point => point.X);
            var ay = a.Min(point => point.Y);
            var ar = a.Max(point => point.X);
            var ab = a.Max(point => point.Y);
            var bx = b.Min(point => point.X);
            var by = b.Min(point => point.Y);
            var br = b.Max(point => point.X);
            var bb = b.Max(point => point.Y);
            var area = Math.Min((ar - ax) * (ab - ay), (br - bx) * (bb - by));
            if (area > 0)
            {
                var overlap = Math.Max(0, Math.Min(ar, br) - Math.Max(ax, bx)) *
                              Math.Max(0, Math.Min(ab, bb) - Math.Max(ay, by));
                if (overlap < area * 0.5) return false;
            }

            var tolerance = area > 0 ? Math.Max(8, Math.Max(ar - ax, ab - ay) * 0.2) : 8;
            return Math.Abs(a.Average(point => point.X) - b.Average(point => point.X)) <= tolerance &&
                   Math.Abs(a.Average(point => point.Y) - b.Average(point => point.Y)) <= tolerance;
        }

        private static CapturedFrame Read(PixelRect region,
            IReadOnlyList<(PixelRect Region, byte[] Pixels)> frames, int scale, CancellationToken token)
        {
            if (scale == 1)
                foreach (var frame in frames)
                    if (frame.Region.Intersect(region) == region)
                    {
                        var start = checked(((region.Y - frame.Region.Y) * frame.Region.Width + region.X -
                                             frame.Region.X) * 4);
                        var stride = checked(frame.Region.Width * 4);
                        var length = checked((region.Height - 1) * stride + region.Width * 4);
                        return Create(region.Width, region.Height, stride, frame.Pixels.AsMemory(start, length));
                    }

            var width = (region.Width + scale - 1) / scale;
            var height = (region.Height + scale - 1) / scale;
            var pixels = new byte[checked(width * height * 4)];
            foreach (var frame in frames)
            {
                var intersection = region.Intersect(frame.Region);
                if (intersection.IsEmpty) continue;
                var top = (intersection.Y - region.Y + scale - 1) / scale;
                var bottom = Math.Min(height, (intersection.Bottom - region.Y + scale - 1) / scale);
                var left = (intersection.X - region.X + scale - 1) / scale;
                var right = Math.Min(width, (intersection.Right - region.X + scale - 1) / scale);
                for (var y = top; y < bottom; y++)
                {
                    token.ThrowIfCancellationRequested();
                    if (scale == 1)
                    {
                        var from = ((region.Y + y - frame.Region.Y) * frame.Region.Width + region.X + left -
                                    frame.Region.X) * 4;
                        frame.Pixels.AsSpan(from, (right - left) * 4).CopyTo(pixels.AsSpan((y * width + left) * 4));
                    }
                    else
                    {
                        for (var x = left; x < right; x++)
                        {
                            var from = ((region.Y + y * scale - frame.Region.Y) * frame.Region.Width + region.X +
                                x * scale - frame.Region.X) * 4;
                            frame.Pixels.AsSpan(from, 4).CopyTo(pixels.AsSpan((y * width + x) * 4, 4));
                        }
                    }
                }
            }

            return Create(width, height, width * 4, pixels);
        }

        private static CapturedFrame Create(int width, int height, int stride, ReadOnlyMemory<byte> pixels)
        {
            return new(width, height, stride, CapturePixelFormat.Bgra8Srgb, pixels, new(false), CaptureAlphaMode.Opaque,
                0, []);
        }
    }
}

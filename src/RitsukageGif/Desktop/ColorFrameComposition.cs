using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static class ColorFrameComposition
    {
        public static CapturedFrameSet Compose(IReadOnlyList<(PixelRect Bounds, CapturedFrameSet Frame)> sources,
            PixelRect region, int width, int height, long revision, CancellationToken token)
        {
            if (region.IsEmpty || width <= 0 || height <= 0)
                throw new ArgumentException("The capture region and output dimensions must be positive.",
                    nameof(region));
            var hdr = new byte[checked(width * height * 8)];
            var half = MemoryMarshal.Cast<byte, Half>(hdr.AsSpan());
            for (var i = 3; i < half.Length; i += 4) half[i] = (Half)1;
            List<FrameColorRegion> regions = [];
            foreach (var (bounds, frame) in sources)
            {
                token.ThrowIfCancellationRequested();
                var clipped = region.Intersect(bounds);
                if (clipped.IsEmpty) continue;
                var raw = frame.Hdr ??
                          throw new NotSupportedException("The source did not supply a linear floating-point frame.");
                if (raw.PixelFormat != CapturePixelFormat.Rgba16FloatScRgb || raw.Width <= 0 || raw.Height <= 0 ||
                    raw.Stride < (long)raw.Width * 8 || raw.Pixels.Length < (long)raw.Stride * raw.Height)
                    throw new CaptureSourceLostException();
                raw = Premultiply(raw, token);
                var outputBounds = Transform(clipped, region, width, height);
                if (raw.ColorRegions.Count == 0) regions.Add(new(outputBounds, raw.ColorInfo));
                else
                    foreach (var (sourceRegion, sourceColorInfo) in raw.ColorRegions)
                    {
                        var colorLeft = bounds.X + (int)Math.Floor((double)sourceRegion.X * bounds.Width / raw.Width);
                        var colorTop = bounds.Y + (int)Math.Floor((double)sourceRegion.Y * bounds.Height / raw.Height);
                        var colorRight = bounds.X +
                                         (int)Math.Ceiling((double)sourceRegion.Right * bounds.Width / raw.Width);
                        var colorBottom = bounds.Y +
                                          (int)Math.Ceiling((double)sourceRegion.Bottom * bounds.Height / raw.Height);
                        var colorBounds = new PixelRect(colorLeft, colorTop, colorRight - colorLeft,
                            colorBottom - colorTop).Intersect(clipped);
                        if (!colorBounds.IsEmpty)
                            regions.Add(new(Transform(colorBounds, region, width, height), sourceColorInfo));
                    }

                if (width == region.Width && height == region.Height && raw.Width == bounds.Width &&
                    raw.Height == bounds.Height)
                {
                    for (var y = outputBounds.Y; y < outputBounds.Bottom; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        var sourceY = region.Y + y - bounds.Y;
                        var sourceX = region.X + outputBounds.X - bounds.X;
                        raw.Pixels.Span.Slice(sourceY * raw.Stride + sourceX * 8, outputBounds.Width * 8)
                            .CopyTo(hdr.AsSpan((y * width + outputBounds.X) * 8));
                    }

                    continue;
                }

                var rawPixels = MemoryMarshal.Cast<byte, Half>(raw.Pixels.Span);
                for (var y = outputBounds.Y; y < outputBounds.Bottom; y++)
                {
                    token.ThrowIfCancellationRequested();
                    var sy = Math.Clamp(
                        (region.Y + (y + 0.5) * region.Height / height - bounds.Y) * raw.Height / bounds.Height - 0.5,
                        0,
                        raw.Height - 1);
                    var y0 = (int)sy;
                    var y1 = Math.Min(y0 + 1, raw.Height - 1);
                    var fy = (float)(sy - y0);
                    for (var x = outputBounds.X; x < outputBounds.Right; x++)
                    {
                        var sx = Math.Clamp(
                            (region.X + (x + 0.5) * region.Width / width - bounds.X) * raw.Width / bounds.Width - 0.5,
                            0,
                            raw.Width - 1);
                        var x0 = (int)sx;
                        var x1 = Math.Min(x0 + 1, raw.Width - 1);
                        var fx = (float)(sx - x0);
                        var destination = (y * width + x) * 4;
                        for (var channel = 0; channel < 4; channel++)
                            half[destination + channel] = (Half)Interpolate(
                                (float)rawPixels[y0 * raw.Stride / 2 + x0 * 4 + channel],
                                (float)rawPixels[y0 * raw.Stride / 2 + x1 * 4 + channel],
                                (float)rawPixels[y1 * raw.Stride / 2 + x0 * 4 + channel],
                                (float)rawPixels[y1 * raw.Stride / 2 + x1 * 4 + channel], fx, fy);
                    }
                }
            }

            var color = new FrameColorInfo(regions.Any(item => item.ColorInfo.IsHdrSource));
            var hdrFrame = new CapturedFrame(width, height, width * 8, CapturePixelFormat.Rgba16FloatScRgb, hdr, color,
                CaptureAlphaMode.Premultiplied, revision, regions);
            return new(HdrToneMapping.Map(hdrFrame, token: token), hdrFrame,
                sources.Count > 0 ? sources.Max(item => item.Frame.TimestampTicks) : Stopwatch.GetTimestamp());
        }

        private static PixelRect Transform(PixelRect clipped, PixelRect region, int width, int height)
        {
            var left = Math.Clamp((int)Math.Ceiling((clipped.X - region.X) * (double)width / region.Width - 0.5), 0,
                width);
            var top = Math.Clamp((int)Math.Ceiling((clipped.Y - region.Y) * (double)height / region.Height - 0.5), 0,
                height);
            var right = Math.Clamp((int)Math.Ceiling((clipped.Right - region.X) * (double)width / region.Width - 0.5),
                0, width);
            var bottom =
                Math.Clamp((int)Math.Ceiling((clipped.Bottom - region.Y) * (double)height / region.Height - 0.5), 0,
                    height);
            return new(left, top, right - left, bottom - top);
        }

        private static CapturedFrame Premultiply(CapturedFrame frame, CancellationToken token)
        {
            if (frame.AlphaMode == CaptureAlphaMode.Premultiplied) return frame;
            var pixels = frame.Pixels.ToArray();
            for (var y = 0; y < frame.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                var row = MemoryMarshal.Cast<byte, Half>(pixels.AsSpan(y * frame.Stride, frame.Width * 8));
                for (var x = 0; x < row.Length; x += 4)
                    if (frame.AlphaMode == CaptureAlphaMode.Opaque)
                    {
                        row[x + 3] = (Half)1;
                    }
                    else
                    {
                        var alpha = Math.Clamp((float)row[x + 3], 0, 1);
                        row[x] = (Half)((float)row[x] * alpha);
                        row[x + 1] = (Half)((float)row[x + 1] * alpha);
                        row[x + 2] = (Half)((float)row[x + 2] * alpha);
                        row[x + 3] = (Half)alpha;
                    }
            }

            return frame with { Pixels = pixels, AlphaMode = CaptureAlphaMode.Premultiplied };
        }

        private static float Interpolate(float a, float b, float c, float d, float x, float y)
        {
            return (a + (b - a) * x) * (1 - y) + (c + (d - c) * x) * y;
        }
    }
}

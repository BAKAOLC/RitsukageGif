using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class ScreenshotOutputTransform
    {
        private static CapturedFrame ResizeHdr(CapturedFrame source, int width, int height, CancellationToken token)
        {
            if (source.Width == width && source.Height == height) return source;
            if (source.PixelFormat != CapturePixelFormat.Rgba16FloatScRgb || source.Width <= 0 || source.Height <= 0 ||
                source.Stride < (long)source.Width * 8 || source.Pixels.Length < (long)source.Stride * source.Height ||
                (long)source.Stride * source.Height > 512L * 1024 * 1024 || !Enum.IsDefined(source.AlphaMode))
                throw new InvalidDataException("Invalid linear HDR frame.");
            if ((long)width * height * 8 > 512L * 1024 * 1024)
                throw new ArgumentException("The HDR screenshot output exceeds the pixel limit.");
            var output = new byte[checked(width * height * 8)];
            var destination = MemoryMarshal.Cast<byte, Half>(output.AsSpan());
            var pixels = MemoryMarshal.Cast<byte, Half>(source.Pixels.Span);
            var footprintWidth = Math.Max(1, (double)source.Width / width);
            var footprintHeight = Math.Max(1, (double)source.Height / height);
            Span<double> sums = stackalloc double[4];
            for (var y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                var yStart = Math.Max(0, (y + 0.5) * source.Height / height - footprintHeight / 2);
                var yEnd = Math.Min(source.Height, (y + 0.5) * source.Height / height + footprintHeight / 2);
                for (var x = 0; x < width; x++)
                {
                    sums.Clear();
                    var xStart = Math.Max(0, (x + 0.5) * source.Width / width - footprintWidth / 2);
                    var xEnd = Math.Min(source.Width, (x + 0.5) * source.Width / width + footprintWidth / 2);
                    double weightSum = 0;
                    for (var sy = (int)yStart; sy < Math.Ceiling(yEnd); sy++)
                    {
                        token.ThrowIfCancellationRequested();
                        var wy = Math.Min(sy + 1, yEnd) - Math.Max(sy, yStart);
                        for (var sx = (int)xStart; sx < Math.Ceiling(xEnd); sx++)
                        {
                            if ((sx & 1023) == 0) token.ThrowIfCancellationRequested();
                            var weight = wy * (Math.Min(sx + 1, xEnd) - Math.Max(sx, xStart));
                            var offset = sy * source.Stride / 2 + sx * 4;
                            var alpha = source.AlphaMode == CaptureAlphaMode.Opaque ? 1 : (float)pixels[offset + 3];
                            var multiplier = source.AlphaMode == CaptureAlphaMode.Straight ? alpha : 1;
                            for (var channel = 0; channel < 3; channel++)
                                sums[channel] += (float)pixels[offset + channel] * multiplier * weight;
                            sums[3] += alpha * weight;
                            weightSum += weight;
                        }
                    }

                    var outputOffset = (y * width + x) * 4;
                    for (var channel = 0; channel < 4; channel++)
                        destination[outputOffset + channel] = (Half)(sums[channel] / weightSum);
                }
            }

            var regions = new List<FrameColorRegion>(source.ColorRegions.Count);
            foreach (var region in source.ColorRegions)
            {
                var bounds = region.Region.Intersect(new(0, 0, source.Width, source.Height));
                if (bounds.IsEmpty) continue;
                var left = (int)Math.Floor((double)bounds.X * width / source.Width);
                var top = (int)Math.Floor((double)bounds.Y * height / source.Height);
                var right = (int)Math.Ceiling((double)bounds.Right * width / source.Width);
                var bottom = (int)Math.Ceiling((double)bounds.Bottom * height / source.Height);
                regions.Add(region with { Region = new(left, top, right - left, bottom - top) });
            }

            return source with
            {
                Width = width, Height = height, Stride = checked(width * 8), Pixels = output,
                AlphaMode = source.AlphaMode == CaptureAlphaMode.Opaque
                    ? CaptureAlphaMode.Opaque
                    : CaptureAlphaMode.Premultiplied,
                ColorRegions = regions.AsReadOnly(),
            };
        }
    }
}

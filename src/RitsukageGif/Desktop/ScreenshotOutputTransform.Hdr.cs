using System.Runtime.InteropServices;
using Avalonia;

namespace RitsukageGif.Desktop
{
    internal static partial class ScreenshotOutputTransform
    {
        internal static CapturedFrame ResizeHdr(CapturedFrame source, int width, int height, CancellationToken token,
            Rect? sourceRegion = null)
        {
            if (sourceRegion is null && source.Width == width && source.Height == height) return source;
            if (source.PixelFormat != CapturePixelFormat.Rgba16FloatScRgb || source.Width <= 0 || source.Height <= 0 ||
                source.Stride < (long)source.Width * 8 || source.Pixels.Length < (long)source.Stride * source.Height ||
                (long)source.Stride * source.Height > 512L * 1024 * 1024 || !Enum.IsDefined(source.AlphaMode))
                throw new InvalidDataException("Invalid linear HDR frame.");
            if (width <= 0 || height <= 0 || (long)width * height * 8 > 512L * 1024 * 1024)
                throw new ArgumentException("The HDR screenshot output exceeds the pixel limit.");
            var region = sourceRegion ?? new Rect(0, 0, source.Width, source.Height);
            if (!double.IsFinite(region.X) || !double.IsFinite(region.Y) || !double.IsFinite(region.Width) ||
                !double.IsFinite(region.Height) || region.Width <= 0 || region.Height <= 0 ||
                region.X < 0 || region.Y < 0 || region.Right > source.Width || region.Bottom > source.Height)
                throw new ArgumentOutOfRangeException(nameof(sourceRegion));
            var output = new byte[checked(width * height * 8)];
            var destination = MemoryMarshal.Cast<byte, Half>(output.AsSpan());
            var pixels = MemoryMarshal.Cast<byte, Half>(source.Pixels.Span);
            var footprintWidth = Math.Max(1, region.Width / width);
            var footprintHeight = Math.Max(1, region.Height / height);
            Span<double> sums = stackalloc double[4];
            for (var y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                var yStart = Math.Max(0, region.Y + (y + 0.5) * region.Height / height - footprintHeight / 2);
                var yEnd = Math.Min(source.Height, region.Y + (y + 0.5) * region.Height / height + footprintHeight / 2);
                for (var x = 0; x < width; x++)
                {
                    sums.Clear();
                    var xStart = Math.Max(0, region.X + (x + 0.5) * region.Width / width - footprintWidth / 2);
                    var xEnd = Math.Min(source.Width, region.X + (x + 0.5) * region.Width / width + footprintWidth / 2);
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
            foreach (var colorRegion in source.ColorRegions)
            {
                var bounds = new Rect(colorRegion.Region.X, colorRegion.Region.Y,
                    colorRegion.Region.Width, colorRegion.Region.Height).Intersect(region);
                if (bounds.Width <= 0 || bounds.Height <= 0) continue;
                var left = (int)Math.Floor((bounds.X - region.X) * width / region.Width);
                var top = (int)Math.Floor((bounds.Y - region.Y) * height / region.Height);
                var right = (int)Math.Ceiling((bounds.Right - region.X) * width / region.Width);
                var bottom = (int)Math.Ceiling((bounds.Bottom - region.Y) * height / region.Height);
                regions.Add(colorRegion with { Region = new(left, top, right - left, bottom - top) });
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

namespace RitsukageGif.Core
{
    public static partial class ImageInpainting
    {
        private static PixelRect MaskRegion(ushort[] coverage, int width, int height, int radius,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(coverage);
            if (coverage.Length != (long)width * height)
                throw new ArgumentException("The image and inpainting mask dimensions must match.", nameof(coverage));
            if (radius is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(radius));
            var left = width;
            var top = height;
            var right = 0;
            var bottom = 0;
            for (var y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < width; x++)
                {
                    if (coverage[y * width + x] == 0) continue;
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x + 1);
                    bottom = Math.Max(bottom, y + 1);
                }
            }

            if (left == width) return default;
            var margin = radius + 3;
            left = Math.Max(0, left - margin);
            top = Math.Max(0, top - margin);
            right = Math.Min(width, right + margin);
            bottom = Math.Min(height, bottom + margin);
            return new(left, top, right - left, bottom - top);
        }

        private static ushort[] CropCoverage(ushort[] coverage, int width, PixelRect region)
        {
            var result = new ushort[checked(region.Width * region.Height)];
            for (var y = 0; y < region.Height; y++)
                Array.Copy(coverage, (region.Y + y) * width + region.X, result, y * region.Width, region.Width);
            return result;
        }
    }
}

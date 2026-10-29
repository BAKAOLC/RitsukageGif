namespace RitsukageGif.Desktop
{
    internal static class PinnedImageShadowRaster
    {
        internal static byte[] Create(int width, int height, int margin, int side, uint color)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(margin);
            if (side is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(side));
            var horizontal = side < 2;
            var columns = horizontal ? checked(width + margin * 2) : margin;
            var rows = horizontal ? margin : height;
            var pixels = new byte[checked(columns * rows * 4)];
            var sigma = margin / 3d;
            var denominator = 2 * sigma * sigma;
            var fade = new double[margin];
            for (var index = 0; index < margin; index++)
                fade[index] = Math.Exp(-((index + .5) * (index + .5)) / denominator);
            for (var y = 0; y < rows; y++)
            {
                var factorY = horizontal ? fade[side == 0 ? margin - y - 1 : y] : 1;
                for (var x = 0; x < columns; x++)
                {
                    var factorX = horizontal
                        ? x < margin ? fade[margin - x - 1] : x >= margin + width ? fade[x - margin - width] : 1
                        : fade[side == 2 ? margin - x - 1 : x];
                    var alpha = (byte)Math.Round((color >> 24) * factorX * factorY);
                    var offset = (y * columns + x) * 4;
                    pixels[offset] = (byte)((color & 255) * alpha / 255);
                    pixels[offset + 1] = (byte)(((color >> 8) & 255) * alpha / 255);
                    pixels[offset + 2] = (byte)(((color >> 16) & 255) * alpha / 255);
                    pixels[offset + 3] = alpha;
                }
            }

            return pixels;
        }
    }
}

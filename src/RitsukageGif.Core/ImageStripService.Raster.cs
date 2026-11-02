namespace RitsukageGif.Core
{
    public static partial class ImageStripService
    {
        private static PngRaster Delete(PngRaster raster, ImageStripAxis axis, int start, int count,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(raster);
            token.ThrowIfCancellationRequested();
            ValidateBand(raster.Width, raster.Height, axis, start, count);
            var channels = raster.ColorType switch
            {
                0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4,
                _ => throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(raster)),
            };
            var bytesPerPixel = Math.Max(1, channels * raster.Depth / 8);
            var width = raster.Width - (axis == ImageStripAxis.Columns ? count : 0);
            var height = raster.Height - (axis == ImageStripAxis.Rows ? count : 0);
            var samples = RemoveSamples(raster.Samples, raster.Width, raster.Height,
                checked(raster.Width * bytesPerPixel), bytesPerPixel, axis, start, count, token);
            return raster with { Width = width, Height = height, Samples = samples };
        }

        private static byte[] RemoveSamples(ReadOnlySpan<byte> source, int width, int height, int stride,
            int bytesPerPixel, ImageStripAxis axis, int start, int count, CancellationToken token)
        {
            var outputWidth = width - (axis == ImageStripAxis.Columns ? count : 0);
            var outputHeight = height - (axis == ImageStripAxis.Rows ? count : 0);
            var outputStride = checked(outputWidth * bytesPerPixel);
            var pixels = new byte[checked(outputStride * outputHeight)];
            for (var y = 0; y < outputHeight; y++)
            {
                token.ThrowIfCancellationRequested();
                var sourceY = axis == ImageStripAxis.Rows && y >= start ? y + count : y;
                var row = source.Slice(checked(sourceY * stride), checked(width * bytesPerPixel));
                var destination = pixels.AsSpan(y * outputStride, outputStride);
                if (axis == ImageStripAxis.Rows)
                {
                    row.CopyTo(destination);
                }
                else
                {
                    var first = checked(start * bytesPerPixel);
                    row[..first].CopyTo(destination);
                    row[((start + count) * bytesPerPixel)..].CopyTo(destination[first..]);
                }
            }

            return pixels;
        }
    }
}

namespace RitsukageGif.Core
{
    public static partial class Png16PixelProcessor
    {
        public static PngRaster CropRgba(PngRaster source, PixelRect region, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (source.Depth != 16 || source.ColorType != 6)
                throw new ArgumentException("RGBA16 is required.", nameof(source));
            Validate(source.Samples, source.Width, source.Height);
            if (region.IsEmpty || region.X < 0 || region.Y < 0 || (long)region.X + region.Width > source.Width ||
                (long)region.Y + region.Height > source.Height) throw new ArgumentOutOfRangeException(nameof(region));
            token.ThrowIfCancellationRequested();
            if (region == new PixelRect(0, 0, source.Width, source.Height)) return source;
            var samples = new byte[checked(region.Width * region.Height * 8)];
            for (var y = 0; y < region.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                source.Samples.AsSpan(((region.Y + y) * source.Width + region.X) * 8, region.Width * 8)
                    .CopyTo(samples.AsSpan(y * region.Width * 8, region.Width * 8));
            }

            return source with { Width = region.Width, Height = region.Height, Samples = samples };
        }
    }
}

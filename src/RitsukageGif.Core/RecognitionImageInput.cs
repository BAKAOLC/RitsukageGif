using ImageMagick;

namespace RitsukageGif.Core
{
    internal static class RecognitionImageInput
    {
        private const int MaximumEncodedBytes = 64 * 1024 * 1024;
        private const long MaximumPixels = 32 * 1024 * 1024;

        internal static MagickImage Decode(ReadOnlyMemory<byte> encoded, long maximumPixels = MaximumPixels)
        {
            if (encoded.IsEmpty || encoded.Length > MaximumEncodedBytes)
                throw new ArgumentOutOfRangeException(nameof(encoded),
                    "Recognition input must contain between 1 byte and 64 MiB.");
            var image = new MagickImage();
            try
            {
                var settings = new MagickReadSettings { FrameCount = 1 };
                image.Ping(encoded.Span, settings);
                if (image.Width == 0 || image.Height == 0 || (long)image.Width * image.Height > maximumPixels)
                    throw new ArgumentOutOfRangeException(nameof(encoded),
                        $"Recognition input must not exceed {maximumPixels / (1024 * 1024)} megapixels.");
                image.Read(encoded.Span, settings);
                image.AutoOrient();
                image.BackgroundColor = MagickColors.White;
                image.Alpha(AlphaAction.Remove);
                image.ColorSpace = ColorSpace.sRGB;
                image.Depth = 8;
                return image;
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }
    }
}

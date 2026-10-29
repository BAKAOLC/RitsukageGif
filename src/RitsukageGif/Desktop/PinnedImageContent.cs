using System.Security.Cryptography;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial record PinnedImageContent(
        byte[] Png,
        int Width,
        int Height,
        byte[] Pixels,
        byte[] ThumbnailPng)
    {
        private const long MaximumPixels = 128L * 1024 * 1024;
        public const int MaximumEncodedBytes = 256 * 1024 * 1024;
        public string ContentHash { get; private init; } = Hash(Png);

        private static string Hash(ReadOnlySpan<byte> png)
        {
            return Convert.ToHexString(SHA256.HashData(png));
        }

        public static PinnedImageContent Decode(ReadOnlyMemory<byte> encoded, CancellationToken token = default)
        {
            if (encoded.IsEmpty || encoded.Length > MaximumEncodedBytes)
                throw new ArgumentOutOfRangeException(nameof(encoded));
            token.ThrowIfCancellationRequested();
            try
            {
                if (TryDecodeHdr(encoded, token) is { } hdr) return hdr;
            }
            catch (MagickException) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }

            using var image = new MagickImage();
            image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
            var settings = new MagickReadSettings { FrameCount = 1 };
            try
            {
                image.Ping(encoded.Span, settings);
                ValidateSize(image);
                token.ThrowIfCancellationRequested();
                image.Read(encoded.Span, settings);
                var preservePng = image is
                    { Format: MagickFormat.Png, Orientation: Orientation.Undefined or Orientation.TopLeft };
                image.AutoOrient();
                ValidateSize(image);
                image.Depth = 8;
                var hasColorProfile = image.GetColorProfile() is not null;
                if (hasColorProfile && !image.TransformColorSpace(ColorProfiles.SRGB))
                    throw new InvalidDataException();
                image.ColorSpace = ColorSpace.sRGB;
                var width = checked((int)image.Width);
                var height = checked((int)image.Height);
                var png = preservePng ? encoded.ToArray() : image.ToByteArray(MagickFormat.Png);
                var sdrPng = preservePng && hasColorProfile ? image.ToByteArray(MagickFormat.Png) : null;
                var pixels = image.ToByteArray(MagickFormat.Bgra);
                var scale = Math.Min(1, 2048d / Math.Max(width, height));
                if (scale < 1)
                    image.Resize(new MagickGeometry((uint)Math.Max(1, Math.Round(width * scale)),
                        (uint)Math.Max(1, Math.Round(height * scale))) { IgnoreAspectRatio = true });
                var thumbnail = scale < 1 ? image.ToByteArray(MagickFormat.Png) : png;
                token.ThrowIfCancellationRequested();
                return new(png, width, height, pixels, thumbnail) { SdrPng = sdrPng };
            }
            catch (MagickException) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }
        }

        private static void ValidateSize(MagickImage image)
        {
            if (image.Width == 0 || image.Height == 0 || (long)image.Width * image.Height > MaximumPixels)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"));
        }
    }
}

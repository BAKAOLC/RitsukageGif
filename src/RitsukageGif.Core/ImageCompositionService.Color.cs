using ImageMagick;

namespace RitsukageGif.Core
{
    public sealed partial class ImageCompositionService
    {
        private static ReadOnlySpan<byte> WorkingPngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

        public static MagickImage ReadSdrWorkingImage(string path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                bool png;
                using (var source = File.OpenRead(path))
                {
                    Span<byte> header = stackalloc byte[8];
                    png = source.Read(header) == 8 && header.SequenceEqual(WorkingPngSignature);
                }

                return png
                    ? NormalizeDecodedSdrImage(PngAnimation.ReadFrames(path, token).First(), token)
                    : ReadMagickWorkingImage(path, null, token);
            }
            catch (MagickException) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }
        }

        public static MagickImage ReadSdrWorkingImage(byte[] encoded, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return encoded.AsSpan().StartsWith(WorkingPngSignature)
                    ? NormalizeDecodedSdrImage(PngAnimation.ReadFrames(encoded, token).First(), token)
                    : ReadMagickWorkingImage(null, encoded, token);
            }
            catch (MagickException) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }
        }

        private static MagickImage ReadMagickWorkingImage(string? path, byte[]? encoded, CancellationToken token)
        {
            var image = new MagickImage();
            try
            {
                image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
                if (path is not null) image.Read(path);
                else image.Read(encoded!);
            }
            catch
            {
                image.Dispose();
                throw;
            }

            return NormalizeDecodedSdrImage(image, token);
        }

        private static MagickImage NormalizeDecodedSdrImage(MagickImage image, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                NormalizeSdrWorkingImage(image);
                token.ThrowIfCancellationRequested();
                return image;
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }

        internal static void NormalizeSdrWorkingImage(IMagickImage<byte> image)
        {
            if (image.GetColorProfile() is not null)
            {
                if (!image.TransformColorSpace(ColorProfiles.SRGB))
                    throw new InvalidDataException("The image color profile could not be converted to sRGB.");
            }
            else
            {
                image.ColorSpace = ColorSpace.sRGB;
            }

            image.SetProfile(ColorProfiles.SRGB);
        }
    }
}

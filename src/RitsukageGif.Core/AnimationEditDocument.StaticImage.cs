using ImageMagick;

namespace RitsukageGif.Core
{
    public sealed partial class AnimationEditDocument
    {
        public static AnimationEditDocument FromImage(ReadOnlyMemory<byte> encodedImage,
            CancellationToken token = default)
        {
            return CreateStaticImage(null, encodedImage, token);
        }

        public static AnimationEditDocument OpenImage(string path, CancellationToken token = default)
        {
            return CreateStaticImage(path, default, token);
        }

        private static AnimationEditDocument CreateStaticImage(string? path, ReadOnlyMemory<byte> encodedImage,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var document = new AnimationEditDocument();
            try
            {
                using var image = new MagickImage();
                if (path is null) image.Ping(encodedImage.Span);
                else image.Ping(path);
                ValidateSize(checked((int)image.Width), checked((int)image.Height));
                token.ThrowIfCancellationRequested();
                if (path is null) image.Read(encodedImage.Span);
                else image.Read(path);
                image.AutoOrient();
                document._current = new(Array.AsReadOnly<AnimationEditFrame>([document.Store(image, 100, token)]), 0);
                return document;
            }
            catch
            {
                document.Dispose();
                throw;
            }
        }
    }
}

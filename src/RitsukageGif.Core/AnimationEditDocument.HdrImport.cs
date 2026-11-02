using ImageMagick;

namespace RitsukageGif.Core
{
    // ReSharper disable once ClassCannotBeInstantiated
    public sealed partial class AnimationEditDocument
    {
        private static MagickImage ReadEditingImage(string path, CancellationToken token)
        {
            if (!PngAnimation.HasHdrPqMetadata(path, token))
                return ImageCompositionService.ReadSdrWorkingImage(path, token);
            var first = PngAnimation.ReadToneMappedFrames(path, token).First();
            return CreateSdrImage(first.Frame);
        }

        private static MagickImage ReadEditingImage(byte[] png, CancellationToken token)
        {
            if (PngAnimation.HasHdrPqMetadata(png, token))
                return CreateSdrImage(PngAnimation.DecodeToneMappedImage(png, token));
            return ImageCompositionService.ReadSdrWorkingImage(png, token);
        }

        private static MagickImage CreateSdrImage(CapturedFrame frame)
        {
            var image = new MagickImage(CapturedFramePixels.ToStraightBgra(frame),
                new PixelReadSettings((uint)frame.Width, (uint)frame.Height, StorageType.Char, PixelMapping.BGRA));
            image.ColorSpace = ColorSpace.sRGB;
            image.SetProfile(ColorProfiles.SRGB);
            return image;
        }
    }
}

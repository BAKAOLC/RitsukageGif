using ImageMagick;

namespace RitsukageGif.Core
{
    internal static partial class MediaPngCodec
    {
        private static void InitializeCanvasColor(MagickImage canvas, IMagickImage<byte> firstFrame)
        {
            canvas.ColorSpace = firstFrame.ColorSpace;
            if (firstFrame.GetColorProfile() is { } profile) canvas.SetProfile(profile);
        }
    }
}

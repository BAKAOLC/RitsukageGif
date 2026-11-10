using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal static class PinnedBitmapSampling
    {
        internal static void Apply(Visual target, bool smooth)
        {
            RenderOptions.SetBitmapInterpolationMode(target,
                smooth ? BitmapInterpolationMode.HighQuality : BitmapInterpolationMode.None);
        }
    }
}

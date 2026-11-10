using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal static class PinnedNativeGroupIcon
    {
        internal static Bitmap Create(PinnedGroupDefinition group, bool active)
        {
            var control = PinnedGroupIcon.Create(new MenuItem(), group, active);
            control.Measure(new(16, 16));
            control.Arrange(new(0, 0, 16, 16));
            var bitmap = new RenderTargetBitmap(new(16, 16), new(96, 96));
            bitmap.Render(control);
            return bitmap;
        }
    }
}

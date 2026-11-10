using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal static class PinnedInputGeometry
    {
        internal static PixelRect? ViewportBounds(Control host, TopLevel window)
        {
            if (host.TransformToVisual(window) is not { } transform) return null;
            var bounds = new Rect(host.Bounds.Size).TransformToAABB(transform);
            var scaling = window.RenderScaling;
            if (host.UseLayoutRounding)
                bounds = new(new(LayoutHelper.RoundLayoutValue(bounds.Left, scaling),
                        LayoutHelper.RoundLayoutValue(bounds.Top, scaling)),
                    new Point(LayoutHelper.RoundLayoutValue(bounds.Right, scaling),
                        LayoutHelper.RoundLayoutValue(bounds.Bottom, scaling)));
            return new((int)(bounds.X * scaling), (int)(bounds.Y * scaling),
                Math.Max(1, (int)(bounds.Width * scaling)), Math.Max(1, (int)(bounds.Height * scaling)));
        }
    }
}

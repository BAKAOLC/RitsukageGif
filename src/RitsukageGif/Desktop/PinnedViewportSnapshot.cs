using Avalonia;

namespace RitsukageGif.Desktop
{
    internal readonly record struct PinnedViewportSnapshot(
        Matrix SourceToTarget,
        PixelSize TargetSize,
        bool UseView,
        double Opacity,
        bool SmoothScaling,
        double RenderScaling,
        PinnedViewportClip? Clip = null);
}

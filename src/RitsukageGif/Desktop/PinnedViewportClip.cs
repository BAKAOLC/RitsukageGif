using Avalonia;

namespace RitsukageGif.Desktop
{
    internal readonly record struct PinnedViewportClip(Rect SourceDisplayRegion, Rect TargetClip);
}

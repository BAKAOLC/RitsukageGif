using Avalonia;

namespace RitsukageGif.Desktop
{
    internal readonly record struct PinnedTransparencySurface(
        PinnedTransparentLayerMode Mode,
        double CellSize,
        Vector SourceOffset,
        bool Dark);
}

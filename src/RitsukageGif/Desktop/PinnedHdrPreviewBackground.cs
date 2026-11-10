namespace RitsukageGif.Desktop
{
    internal readonly record struct PinnedHdrPreviewBackground(
        PinnedTransparentLayerMode Mode,
        PixelRect SourceBounds,
        double CellPixels,
        bool Dark,
        float WhiteLevel);
}

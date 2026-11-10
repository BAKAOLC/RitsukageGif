namespace RitsukageGif.Desktop
{
    internal sealed class CompactSpatialBand(PixelRect bounds)
    {
        internal PixelRect Bounds { get; set; } = bounds;
        internal List<int> Indices { get; } = [];
    }
}

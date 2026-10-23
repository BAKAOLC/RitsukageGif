namespace RitsukageGif.Contracts
{
    // Shared coordinates are physical desktop pixels. Never scale desktop origins.
    public readonly record struct PixelPoint(int X, int Y);
}

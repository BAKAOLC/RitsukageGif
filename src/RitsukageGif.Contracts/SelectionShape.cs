namespace RitsukageGif.Contracts
{
    public sealed record SelectionShape(PixelRect Bounds, string PathData, bool EvenOdd = false);
}

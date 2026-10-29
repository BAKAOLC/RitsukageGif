namespace RitsukageGif.Core
{
    public sealed record DecorationAlphaMask(
        int Width,
        int Height,
        PixelPoint ContentOffset,
        ReadOnlyMemory<byte> Alpha);
}

namespace RitsukageGif.Core
{
    public sealed record EncodedPngFrame(
        byte[] Png,
        int CanvasWidth,
        int CanvasHeight,
        PixelRect Region,
        int DelayMs,
        byte Disposal,
        byte Blend,
        bool Poster);
}

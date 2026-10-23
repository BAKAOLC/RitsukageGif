namespace RitsukageGif.Desktop
{
    internal sealed record CaptureSourceFrame(int Width, int Height, byte[] Pixels, int OriginX = 0, int OriginY = 0)
    {
        public PixelRect Bounds => new(OriginX, OriginY, Width, Height);
    }
}

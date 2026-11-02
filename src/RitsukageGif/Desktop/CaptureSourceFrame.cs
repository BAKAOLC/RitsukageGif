namespace RitsukageGif.Desktop
{
    internal sealed record CaptureSourceFrame(int Width, int Height, byte[] Pixels, int OriginX = 0, int OriginY = 0)
    {
        public double PixelDensity { get; init; } = 1;
        public PixelRect? CoordinateBounds { get; init; }
        public PixelRect Bounds => CoordinateBounds ?? new(OriginX, OriginY, Width, Height);
    }
}

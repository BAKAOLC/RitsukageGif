namespace RitsukageGif.Desktop
{
    internal sealed class CaptureHistoryHdrData
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int Stride { get; set; }
        public CapturePixelFormat PixelFormat { get; set; }
        public CaptureAlphaMode AlphaMode { get; set; }
        public long Revision { get; set; }
        public long PixelBytes { get; set; }
        public FrameColorInfo ColorInfo { get; set; } = new(false);
        public List<FrameColorRegion> ColorRegions { get; set; } = [];
    }
}

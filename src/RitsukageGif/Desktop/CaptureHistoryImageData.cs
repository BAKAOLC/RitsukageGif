namespace RitsukageGif.Desktop
{
    internal sealed class CaptureHistoryImageData
    {
        public Guid Id { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public PixelRect Region { get; set; }
        public CaptureSourceKind Source { get; set; }
        public long PngBytes { get; set; }
        public double CaptureScaling { get; set; } = 1;
        public CaptureDecoration Decoration { get; set; } = CaptureDecoration.None;
        public PixelPoint? OutputOrigin { get; set; }
        public CaptureHistoryHdrData? Hdr { get; set; }
    }
}

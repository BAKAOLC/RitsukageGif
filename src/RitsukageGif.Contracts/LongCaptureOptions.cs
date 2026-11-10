namespace RitsukageGif.Contracts
{
    public sealed record LongCaptureOptions
    {
        public PixelRect Region { get; init; }
        public LongCaptureSourceKind Source { get; init; }
        public long? WindowId { get; init; }
        public ScrollCaptureOptions Stitching { get; init; } = new();
        public double IntervalSeconds { get; init; } = 0.1;
        public HdrToneMapMode ToneMap { get; init; } = HdrToneMapMode.HighlightCompression;
    }
}

namespace RitsukageGif.Contracts
{
    public sealed record StaticCaptureOptions
    {
        public StaticImageSaveOptions Image { get; init; } = new();
        public bool? IncludeCursor { get; init; }
        public int? ScalePercent { get; init; }
        public string? OutputName { get; init; }
        public HdrToneMapMode? ToneMap { get; init; }
    }
}

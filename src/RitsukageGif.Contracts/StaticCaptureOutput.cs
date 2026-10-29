namespace RitsukageGif.Contracts
{
    public sealed record StaticCaptureOutput(string Path)
    {
        public OutputColorMode Color { get; init; }
        public StaticImageSaveOptions Image { get; init; } = new();
        public HdrImageSaveOptions HdrImage { get; init; } = new();
    }
}

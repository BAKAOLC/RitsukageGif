namespace RitsukageGif.Contracts
{
    public sealed record ImageCompositionOptions
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public string Background { get; init; } = "transparent";
        public IReadOnlyList<ImageLayerOptions> Layers { get; init; } = [];
        public string? Format { get; init; }
        public int Quality { get; init; } = 90;
        public bool Lossless { get; init; } = true;
        public bool Overwrite { get; init; }
    }
}

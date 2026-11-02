namespace RitsukageGif.Contracts
{
    public sealed record ImageInpaintingOptions
    {
        public IReadOnlyList<PixelRect> Regions { get; init; } = [];
        public string? MaskPath { get; init; }
        public ImageMaskChannel MaskChannel { get; init; }
        public int Radius { get; init; } = 4;
        public int FrameIndex { get; init; }
        public string? Format { get; init; }
        public int Quality { get; init; } = 90;
        public bool Lossless { get; init; } = true;
        public bool Overwrite { get; init; }
    }
}

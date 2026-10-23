namespace RitsukageGif.Contracts
{
    public sealed record MediaProcessOptions
    {
        public string? Format { get; init; }
        public int Colors { get; init; } = 256;
        public int? Width { get; init; }
        public int? Height { get; init; }
        public int Quality { get; init; } = 90;
        public bool Lossless { get; init; } = true;
        public bool Dither { get; init; }
        public bool Overwrite { get; init; }
        public int? CropX { get; init; }
        public int? CropY { get; init; }
        public int? CropWidth { get; init; }
        public int? CropHeight { get; init; }
        public bool FlipHorizontal { get; init; }
        public bool FlipVertical { get; init; }
        public int Rotation { get; init; }
        public int? FrameIndex { get; init; }
        public IReadOnlyList<PixelEffect>? Effects { get; init; }
    }
}

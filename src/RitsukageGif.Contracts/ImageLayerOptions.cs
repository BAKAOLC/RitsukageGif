namespace RitsukageGif.Contracts
{
    public sealed record ImageLayerOptions
    {
        public required string Path { get; init; }
        public int FrameIndex { get; init; }
        public int X { get; init; }
        public int Y { get; init; }
        public int? Width { get; init; }
        public int? Height { get; init; }
        public int? CropX { get; init; }
        public int? CropY { get; init; }
        public int? CropWidth { get; init; }
        public int? CropHeight { get; init; }
        public double Opacity { get; init; } = 1;
        public int Rotation { get; init; }
        public IReadOnlyList<PixelEffect> Effects { get; init; } = [];
    }
}

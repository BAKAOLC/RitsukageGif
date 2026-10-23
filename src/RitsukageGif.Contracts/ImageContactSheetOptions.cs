namespace RitsukageGif.Contracts
{
    public sealed record ImageContactSheetOptions
    {
        public int Columns { get; init; } = 4;
        public int ThumbnailWidth { get; init; } = 160;
        public int ThumbnailHeight { get; init; } = 120;
        public int FirstFrame { get; init; }
        public int FrameStep { get; init; } = 1;
        public int MaxFrames { get; init; } = 64;
        public string Background { get; init; } = "transparent";
        public bool Overwrite { get; init; }
    }
}

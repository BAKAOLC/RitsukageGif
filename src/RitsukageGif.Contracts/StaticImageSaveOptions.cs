namespace RitsukageGif.Contracts
{
    public sealed record StaticImageSaveOptions
    {
        public StaticImageFormat? Format { get; init; }
        public int Quality { get; init; } = 90;
        public string Background { get; init; } = "#ffffff";
        public bool Overwrite { get; init; }
    }
}

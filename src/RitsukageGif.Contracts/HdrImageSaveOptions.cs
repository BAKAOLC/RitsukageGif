namespace RitsukageGif.Contracts
{
    public sealed record HdrImageSaveOptions
    {
        public HdrImageFormat? Format { get; init; }

        // Direct service callers may explicitly replace an existing image atomically.
        // ReSharper disable once UnusedAutoPropertyAccessor.Global
        public bool Overwrite { get; init; }
    }
}

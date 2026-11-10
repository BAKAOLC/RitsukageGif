namespace RitsukageGif.Contracts
{
    public sealed record TextTranslationOptions
    {
        public string? SourceLanguage { get; init; }
        public string? TargetLanguage { get; init; }
        public int? TimeoutSeconds { get; init; }
    }
}

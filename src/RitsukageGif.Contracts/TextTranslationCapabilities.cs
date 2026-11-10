namespace RitsukageGif.Contracts
{
    public sealed record TextTranslationCapabilities(
        bool Available,
        bool Configured,
        string Provider,
        string SourceLanguage,
        string TargetLanguage,
        int MaximumTextLength,
        string? FailureReason);
}

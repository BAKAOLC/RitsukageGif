namespace RitsukageGif.Contracts
{
    public sealed record OcrCapabilities(
        bool Available,
        int MaxImageDimension,
        IReadOnlyList<OcrLanguage> Languages,
        string? FailureReason);
}

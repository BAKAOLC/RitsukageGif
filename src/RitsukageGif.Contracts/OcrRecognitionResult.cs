namespace RitsukageGif.Contracts
{
    public sealed record OcrRecognitionResult(
        string Text,
        string LanguageTag,
        int Width,
        int Height,
        double? TextAngle,
        IReadOnlyList<OcrLine> Lines);
}

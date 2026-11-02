namespace RitsukageGif.Desktop
{
    internal sealed record ImageTranslationBlock(
        int Id,
        OcrBounds Bounds,
        IReadOnlyList<OcrBounds> Words,
        string TranslatedText);
}

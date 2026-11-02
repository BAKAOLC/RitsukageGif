namespace RitsukageGif.Desktop
{
    internal sealed record ImageTranslationDocument(
        ReadOnlyMemory<byte> SourcePng,
        int Width,
        int Height,
        string SourceLanguage,
        string TargetLanguage,
        string TranslatedText,
        IReadOnlyList<ImageTranslationBlock> Blocks);
}

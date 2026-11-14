namespace RitsukageGif.Desktop
{
    internal sealed record PinnedDocumentTranslationState(string TargetLanguage, string Text, bool Enabled)
    {
        internal bool IsValid => TranslationService.ValidTargetLanguage(TargetLanguage) &&
                                 Text is { Length: <= PinnedContentRegistry.MaximumTextCharacters } &&
                                 !string.IsNullOrWhiteSpace(Text);

        internal long EstimatedBytes => ((long)TargetLanguage.Length + Text.Length) * sizeof(char);
    }
}

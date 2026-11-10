namespace RitsukageGif.Contracts
{
    public interface ITextTranslationService
    {
        Task<TextTranslationCapabilities> GetCapabilitiesAsync(CancellationToken token = default);

        Task<TextTranslationResult> TranslateAsync(string text, TextTranslationOptions? options = null,
            CancellationToken token = default);
    }
}

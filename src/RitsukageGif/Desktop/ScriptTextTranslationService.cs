namespace RitsukageGif.Desktop
{
    internal sealed class ScriptTextTranslationService(
        Func<TranslationPreferences> preferences,
        TranslationService? service = null,
        Func<string, CancellationToken, ValueTask<string?>>? readCredential = null) : ITextTranslationService
    {

        private readonly TranslationService _service = service ?? new();
        private readonly Func<string, CancellationToken, ValueTask<string?>> _readCredential =
            readCredential ?? CredentialVault.ReadAsync;

        public Task<TextTranslationCapabilities> GetCapabilitiesAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var current = preferences() with { };
            var failure = ConfigurationFailure(current);
            return Task.FromResult(new TextTranslationCapabilities(true, failure is null,
                current.Provider.ToString(), current.SourceLanguage, current.TargetLanguage, 100000, failure));
        }

        public async Task<TextTranslationResult> TranslateAsync(string text, TextTranslationOptions? options = null,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var current = preferences() with { };
            if (options?.TimeoutSeconds is { } timeout) current = current with { TimeoutSeconds = timeout };
            if (ConfigurationFailure(current) is { } failure) throw new ArgumentException(failure);
            if (string.IsNullOrWhiteSpace(text) || text.Length > 100000)
                throw new ArgumentException("Translation text must contain between 1 and 100000 characters.",
                    nameof(text));
            var apiKey = "";
            if (current.UseApiKey)
            {
                apiKey = await _readCredential(current.CredentialKey, token) ??
                         throw new InvalidOperationException("The configured translation credential is missing.");
                token.ThrowIfCancellationRequested();
            }

            var result = await _service.TranslateAsync(current.CreateOptions(apiKey),
                new(text, options?.TargetLanguage ?? current.TargetLanguage,
                    options?.SourceLanguage ?? current.SourceLanguage), token);
            token.ThrowIfCancellationRequested();
            return new(result.Text, result.DetectedSourceLanguage);
        }

        private static string? ConfigurationFailure(TranslationPreferences value)
        {
            if (!Enum.IsDefined(value.Provider)) return "Unknown translation provider.";
            if (!Uri.TryCreate(value.Endpoint, UriKind.Absolute, out var endpoint) ||
                endpoint.Scheme is not ("http" or "https") || endpoint.UserInfo.Length != 0 ||
                endpoint.Fragment.Length != 0)
                return "Translation service endpoint is not configured correctly.";
            if (value.TimeoutSeconds is < 1 or > 300)
                return "Translation timeout must be between 1 and 300 seconds.";
            return value.Provider switch
            {
                TranslationProvider.DeepL or TranslationProvider.Azure when !value.UseApiKey =>
                    "The selected translation provider requires an API key.",
                TranslationProvider.OpenAiCompatible when string.IsNullOrWhiteSpace(value.Model) =>
                    "An OpenAI-compatible translation model is required.",
                _ => null,
            };
        }
    }
}

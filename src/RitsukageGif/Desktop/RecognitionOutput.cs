using System.Globalization;

namespace RitsukageGif.Desktop
{
    internal static class RecognitionOutput
    {
        internal static string? Language(RecognitionPreferences preferences, IReadOnlyList<OcrLanguage> languages)
        {
            if (preferences.LanguageTag.Length != 0) return preferences.LanguageTag;
            var current = CultureInfo.CurrentUICulture;
            return languages.FirstOrDefault(language =>
                       string.Equals(language.Tag, current.Name, StringComparison.OrdinalIgnoreCase))?.Tag ??
                   languages.FirstOrDefault(language => language.Tag.StartsWith(current.TwoLetterISOLanguageName + "-",
                       StringComparison.OrdinalIgnoreCase) || string.Equals(language.Tag,
                       current.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))?.Tag;
        }

        internal static async Task<string> RecognizeAsync(ReadOnlyMemory<byte> image,
            RecognitionPreferences preferences,
            ITextRecognitionService service, CancellationToken token)
        {
            preferences = preferences.Normalize();
            var capabilities = await service.GetCapabilitiesAsync(token);
            token.ThrowIfCancellationRequested();
            var result = await service.RecognizeImageAsync(image, Language(preferences, capabilities.Languages), token);
            token.ThrowIfCancellationRequested();
            return RecognitionTextFormatter.Format(result, preferences);
        }
    }
}

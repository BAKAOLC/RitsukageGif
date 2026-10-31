using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal sealed partial class TranslationService
    {
        private static TranslationResult ReadResult(TranslationProvider provider, JsonElement root)
        {
            string? text = null;
            string? detected = null;
            switch (provider)
            {
                case TranslationProvider.LibreTranslate:
                    text = StringProperty(root, "translatedText");
                    if (root.ValueKind == JsonValueKind.Object &&
                        root.TryGetProperty("detectedLanguage", out var language))
                        detected = StringProperty(language, "language");
                    break;
                case TranslationProvider.DeepL:
                    if (First(root, "translations") is { } translation)
                    {
                        text = StringProperty(translation, "text");
                        detected = StringProperty(translation, "detected_source_language");
                    }

                    break;
                case TranslationProvider.Azure:
                    if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 1 &&
                        root[0].ValueKind == JsonValueKind.Object)
                    {
                        var item = root[0];
                        if (First(item, "translations") is { } translated) text = StringProperty(translated, "text");
                        if (item.TryGetProperty("detectedLanguage", out var source))
                            detected = StringProperty(source, "language");
                    }

                    break;
                case TranslationProvider.OpenAiCompatible:
                    if (First(root, "choices") is { } choice &&
                        choice.TryGetProperty("message", out var message) &&
                        StringProperty(choice, "finish_reason") is "stop" or null)
                        text = StringProperty(message, "content");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(provider));
            }

            if (string.IsNullOrWhiteSpace(text))
                throw new TranslationException(TranslationFailureKind.InvalidResponse,
                    "Translation service returned no complete translated text.");
            return new(text, detected);
        }

        private static JsonElement? First(JsonElement root, string property)
        {
            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out var values) &&
                   values.ValueKind == JsonValueKind.Array && values.GetArrayLength() == 1 &&
                   values[0].ValueKind == JsonValueKind.Object
                ? values[0]
                : null;
        }

        private static string? StringProperty(JsonElement root, string property)
        {
            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
    }
}

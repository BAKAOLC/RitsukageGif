using System.Security.Cryptography;
using System.Text;
using RitsukageGif.SettingsGeneration;

namespace RitsukageGif.Desktop
{
    [GenerateSettingsMerge(typeof(AppSettings))]
    public sealed record TranslationPreferences
    {
        public TranslationProvider Provider { get; set; }
        public string Endpoint { get; set; } = "";
        public string Region { get; set; } = "";
        public string Model { get; set; } = "";
        public string SourceLanguage { get; set; } = "auto";
        public string TargetLanguage { get; set; } = "zh";
        public string FallbackLanguage { get; set; } = "en";
        public int TimeoutSeconds { get; set; } = 30;
        public bool UseApiKey { get; set; }
        public TranslationMode Mode { get; set; }
        public TranslationImageDisplayMode ImageDisplayMode { get; set; }
        public bool ShowOriginalText { get; set; } = true;
        public bool WindowTopmost { get; set; }

        internal string CredentialKey => "translation/" + Provider.ToString().ToLowerInvariant() + "/" +
                                         Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Endpoint)));

        internal TranslationPreferences Normalize()
        {
            return this with
            {
                Provider = Enum.IsDefined(Provider) ? Provider : TranslationProvider.LibreTranslate,
                // ReSharper disable ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
                Endpoint = Endpoint?.Trim() ?? "", Region = Region?.Trim() ?? "", Model = Model?.Trim() ?? "",
                SourceLanguage = string.IsNullOrWhiteSpace(SourceLanguage) ? "auto" : SourceLanguage.Trim(),
                TargetLanguage = string.IsNullOrWhiteSpace(TargetLanguage) ? "zh" : TargetLanguage.Trim(),
                FallbackLanguage = FallbackLanguage?.Trim() ?? "",
                // ReSharper restore ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
                TimeoutSeconds = Math.Clamp(TimeoutSeconds, 1, 300),
                Mode = Enum.IsDefined(Mode) ? Mode : TranslationMode.ImageToText,
                ImageDisplayMode = Enum.IsDefined(ImageDisplayMode)
                    ? ImageDisplayMode
                    : TranslationImageDisplayMode.TextAndImage,
            };
        }

        internal TranslationOptions CreateOptions(string apiKey)
        {
            return new()
            {
                Provider = Provider, Endpoint = Endpoint, ApiKey = apiKey,
                Region = Region, Model = Model, TimeoutSeconds = TimeoutSeconds,
            };
        }
    }
}

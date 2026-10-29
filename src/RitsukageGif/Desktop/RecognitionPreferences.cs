namespace RitsukageGif.Desktop
{
    public sealed record RecognitionPreferences
    {
        public RecognitionTextLayout Layout { get; set; }
        public RecognitionPunctuation Punctuation { get; set; }
        public bool ShowDialogOnRecognition { get; set; } = true;
        public bool ShowDialogOnCopyAll { get; set; } = true;
        public string LanguageTag { get; set; } = "";

        public RecognitionPreferences Normalize()
        {
            // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
            var language = LanguageTag?.Trim() ?? "";
            if (language.Length > 85 ||
                language.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
                language = "";
            return this with
            {
                Layout = Enum.IsDefined(Layout) ? Layout : RecognitionTextLayout.Auto,
                Punctuation = Enum.IsDefined(Punctuation) ? Punctuation : RecognitionPunctuation.None,
                LanguageTag = language,
            };
        }
    }
}

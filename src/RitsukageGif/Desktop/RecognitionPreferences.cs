namespace RitsukageGif.Desktop
{
    public sealed record RecognitionPreferences
    {
        public RecognitionTextLayout Layout { get; set; }
        public RecognitionPunctuation Punctuation { get; set; }
        public FormulaCopyFormat CopyFormat { get; set; }
        public FormulaLatexFormat LatexFormat { get; set; } = FormulaLatexFormat.DisplayDollar;
        public bool ShowOriginalImage { get; set; } = true;
        public bool ShowDialogOnRecognition { get; set; } = true;
        public bool ShowDialogOnCopySelected { get; set; } = true;
        public bool ShowDialogOnCopyAll { get; set; } = true;
        public string LanguageTag { get; set; } = "";
        public RecognitionDialogPreferences Dialog { get; set; } = new();

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
                CopyFormat = Enum.IsDefined(CopyFormat) ? CopyFormat : FormulaCopyFormat.Latex,
                LatexFormat = Enum.IsDefined(LatexFormat) ? LatexFormat : FormulaLatexFormat.DisplayDollar,
                LanguageTag = language,
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                Dialog = (Dialog ?? new()).Normalize(),
            };
        }
    }
}

namespace RitsukageGif.Desktop
{
    public sealed partial record AppSettings
    {
        public TranslationPreferences Translation { get; set; } = new();
    }
}

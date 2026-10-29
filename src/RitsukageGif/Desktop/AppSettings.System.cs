namespace RitsukageGif.Desktop
{
    public sealed partial record AppSettings
    {
        public SystemPreferences System { get; set; } = new();
        public RecognitionPreferences Recognition { get; set; } = new();
    }
}

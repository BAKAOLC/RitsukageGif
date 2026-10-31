namespace RitsukageGif.Desktop
{
    internal sealed class TranslationOptions
    {
        public TranslationProvider Provider { get; init; }
        public string Endpoint { get; init; } = "";
        public string ApiKey { get; init; } = "";
        public string Region { get; init; } = "";
        public string Model { get; init; } = "";
        public int TimeoutSeconds { get; init; } = 30;
    }
}

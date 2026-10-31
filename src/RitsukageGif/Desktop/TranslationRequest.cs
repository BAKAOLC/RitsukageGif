namespace RitsukageGif.Desktop
{
    internal sealed record TranslationRequest(string Text, string TargetLanguage, string SourceLanguage = "auto");
}

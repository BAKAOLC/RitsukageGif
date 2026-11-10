namespace RitsukageGif.Desktop
{
    internal sealed record StructuredRecognitionRequest(
        StructuredRecognitionKind Kind,
        string ImagePath,
        StructuredRecognitionRenderFormat RenderFormat = StructuredRecognitionRenderFormat.None,
        float FontSize = 28,
        uint TextColor = 0xff000000,
        bool SourcesArray = false,
        int PixelWidth = 0,
        int PixelHeight = 0,
        string? LanguageTag = null);
}

namespace RitsukageGif.Desktop
{
    internal sealed record StructuredRecognitionRequest(
        bool Formula,
        string ImagePath,
        StructuredRecognitionRenderFormat RenderFormat = StructuredRecognitionRenderFormat.None,
        float FontSize = 28,
        uint TextColor = 0xff000000,
        bool SourcesArray = false);
}

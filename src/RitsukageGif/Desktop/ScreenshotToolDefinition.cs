namespace RitsukageGif.Desktop
{
    internal sealed record ScreenshotToolDefinition(
        ScreenshotTool Tool,
        string NameKey,
        string IconKey,
        IReadOnlyList<AnnotationPropertyDefinition> Properties);
}

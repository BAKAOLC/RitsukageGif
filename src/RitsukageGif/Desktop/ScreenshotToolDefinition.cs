namespace RitsukageGif.Desktop
{
    internal sealed record ScreenshotToolDefinition(
        ScreenshotTool Tool,
        string NameKey,
        string IconKey,
        IReadOnlyList<AnnotationPropertyDefinition> Properties,
        string ObjectKind,
        AnnotationToolStyle DefaultStyle)
    {
        public string StyleKey => ObjectKind;
        public bool SupportsRecording { get; init; } = true;
    }
}

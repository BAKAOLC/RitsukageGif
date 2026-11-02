namespace RitsukageGif.Desktop
{
    internal sealed record ImageEditorDocumentView(
        PinnedImageContent Image,
        CapturedFrame? Hdr,
        ScreenshotAnnotationSession Session,
        ImageEditorCanvas Canvas,
        ScreenshotAnnotationSurface Surface,
        ScreenshotToolbar Toolbar);
}

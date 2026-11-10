namespace RitsukageGif.Desktop
{
    internal sealed record PinnedDocumentState(PinnedWindowState Window, PinnedDocument Document)
    {
        public PinnedDocumentThumbnailState? Thumbnail { get; set; }
        public PinnedDocumentThumbnailRequest? ThumbnailRequest { get; set; }
        public bool IgnoreFormat { get; set; }
        public bool Selectable { get; set; } = true;
        public double Width { get; set; } = 440;
        public double Height { get; set; } = 320;
        public ScreenshotAnnotationDocument? Annotations { get; set; }
        public double AnnotationScaling { get; set; } = 1;
        public PinnedFileListMaximumSize? FileListMaximumSize { get; init; }
    }
}

using System.Text.Json.Serialization;

namespace RitsukageGif.Desktop
{
    internal sealed record PinnedDocumentState(PinnedWindowState Window, PinnedDocument Document)
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public long ClosedAtUtcTicks { get; init; }

        public PinnedDocumentThumbnailState? Thumbnail { get; set; }
        public PinnedDocumentThumbnailRequest? ThumbnailRequest { get; set; }
        public bool IgnoreFormat { get; set; }
        public bool Selectable { get; set; } = true;
        public double Width { get; set; } = 440;
        public double Height { get; set; } = 320;
        public ScreenshotAnnotationDocument? Annotations { get; set; }
        public double AnnotationScaling { get; set; } = 1;
        public PinnedFileListMaximumSize? FileListMaximumSize { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public PinnedDocumentTranslationState? Translation { get; init; }
    }
}

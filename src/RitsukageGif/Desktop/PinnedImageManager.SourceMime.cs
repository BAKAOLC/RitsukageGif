namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private PinnedInputResult PinSourceImage(PinnedClipboardSnapshot snapshot, PinnedImageContent content,
            (CaptureWindowShape? Shape, double CaptureScaling)? metadata = null)
        {
            var source = PinnedSourceMime.Create(snapshot.SourceText, snapshot.SourceHtml);
            var (shape, captureScaling) = metadata ?? (null, 1d);
            return CompleteSourceInput(source, Pin(content, null, "", captureScaling, windowShape: shape));
        }

        private PinnedInputResult PinSourceDocument(PinnedClipboardSnapshot snapshot, PinnedDocument document,
            bool usePlainText)
        {
            if (document is not (PinnedTextDocument or PinnedHtmlDocument))
                return new(true, PinDocument(document));
            var source = DocumentSourceMime(snapshot, usePlainText);
            return CompleteSourceInput(source, PinDocument(document));
        }

        private static PinnedSourceMime? DocumentSourceMime(PinnedClipboardSnapshot snapshot, bool usePlainText)
        {
            return PinnedSourceMime.Create(snapshot.SourceText, usePlainText ? null : snapshot.SourceHtml);
        }

        private static PinnedInputResult CompleteSourceInput(PinnedSourceMime? source, IPinnedWindow? created)
        {
            created?.Presentation.SourceMime = source;
            return new(true, created);
        }
    }
}

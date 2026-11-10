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

        private PinnedInputResult PinSourceDocument(PinnedClipboardSnapshot snapshot, PinnedDocument document)
        {
            if (document is not (PinnedTextDocument or PinnedHtmlDocument))
                return new(true, PinDocument(document));
            var source = PinnedSourceMime.Create(snapshot.SourceText, snapshot.SourceHtml);
            return CompleteSourceInput(source, PinDocument(document));
        }

        private static PinnedInputResult CompleteSourceInput(PinnedSourceMime? source, IPinnedWindow? created)
        {
            created?.Presentation.SourceMime = source;
            return new(true, created);
        }
    }
}

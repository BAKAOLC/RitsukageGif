namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private string AnnotationImagePath(string id)
        {
            if (!Guid.TryParseExact(id, "N", out _))
                throw new InvalidDataException("Invalid annotation image resource identifier.");
            return Path.Combine(_sessionDirectory!, "annotation-" + id + ".image");
        }

        private async Task<ScreenshotAnnotationDocument?> ReadAnnotationImagesAsync(
            ScreenshotAnnotationDocument? document, CancellationToken token)
        {
            if (document is null) return null;
            var images = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var id in document.Layers.Select(layer => layer!.ImageId).OfType<string>().Distinct())
            {
                var path = AnnotationImagePath(id);
                if (new FileInfo(path).Length > 16L * 1024 * 1024)
                    throw new InvalidDataException("The annotation image resource exceeds the size limit.");
                images.Add(id, await File.ReadAllBytesAsync(path, token));
            }

            return document with { Images = images };
        }

        private async Task WriteAnnotationImagesAsync(ScreenshotAnnotationDocument? document, List<string> created,
            CancellationToken token)
        {
            if (document is null) return;
            foreach (var (id, data) in document.Images)
            {
                var path = AnnotationImagePath(id);
                if (!File.Exists(path)) await WriteSessionImageAsync(path, data, created, token);
            }
        }

        private static void ValidateStoredDocumentAnnotations(ScreenshotAnnotationDocument document)
        {
            // ReSharper disable RedundantAlwaysMatchSubpattern
            if (document is not { Layers: not null, Current: not null, Undo: not null, Redo: not null } ||
                document.Layers.Any(layer => layer is not
                {
                    Points: not null, Text: not null, Properties: not null,
                    AutoMosaicRegions: not null, AutoMosaicCandidates: not null,
                }))
                throw new InvalidDataException("Invalid stored document annotation data.");
            // ReSharper restore RedundantAlwaysMatchSubpattern
            ScreenshotAnnotationSession.ValidateDocument(document);
        }

        private void CleanupAnnotationImages(IEnumerable<PinnedImageState> imageStates,
            IEnumerable<PinnedDocumentState> documentStates)
        {
            var documents = imageStates.Concat(_pendingRestore.Values).Concat(_unrestoredStates.Values)
                .Concat(_closedImages.Select(image => image.State)).Select(state => state.Annotations)
                .Concat(documentStates.Concat(_pendingDocuments.Values).Concat(_unrestoredDocuments.Values)
                    .Concat(_closedDocuments.Select(item => item.State)).Select(state => state.Annotations))
                .Concat(_documentWindows.Select(window => window.CaptureDocumentState().Annotations));
            var retained = documents.SelectMany(document => document?.Layers ?? [])
                .Select(layer => layer!.ImageId).OfType<string>().ToHashSet(StringComparer.Ordinal);
            retained.UnionWith(_rejectedSession.AnnotationImageIds);
            foreach (var path in Directory.EnumerateFiles(_sessionDirectory!, "annotation-*.image"))
            {
                var name = Path.GetFileName(path);
                if (name.Length != 49 || !Guid.TryParseExact(name.AsSpan(11, 32), "N", out _) ||
                    retained.Contains(name.Substring(11, 32))) continue;
                File.Delete(path);
            }
        }
    }
}

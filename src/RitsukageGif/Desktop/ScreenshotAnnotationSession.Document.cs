namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        internal ScreenshotAnnotationDocument CaptureDocument()
        {
            var layers = _retained.Concat(_annotations).Distinct().ToArray();
            var indexes = layers.Select((layer, index) => (layer, index)).ToDictionary(pair => pair.layer,
                pair => pair.index);
            var images = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var records = new ScreenshotAnnotationRecord[layers.Length];
            for (var index = 0; index < layers.Length; index++)
            {
                var layer = layers[index];
                if (layer.WatermarkImage is { } image) images.TryAdd(image.Id, image.GetEncoded());
                records[index] = layer.CaptureDocumentRecord();
            }

            return new()
            {
                Layers = records, Current = [.. _annotations.Select(layer => indexes[layer])],
                Undo = [.. _undo.Select(state => state.Select(layer => indexes[layer]).ToArray())],
                Redo = [.. _redo.Select(state => state.Select(layer => indexes[layer]).ToArray())],
                Visible = AnnotationsVisible, Images = images,
            };
        }

        internal void RestoreDocument(ScreenshotAnnotationDocument document)
        {
            ValidateDocument(document);
            var existingImages = _retained.Select(layer => layer.WatermarkImage).OfType<ScreenshotAnnotationImage>()
                .Distinct().ToDictionary(image => image.Id, StringComparer.Ordinal);
            var retainedBytes = existingImages.Values.Sum(image =>
                (long)image.Bitmap.PixelSize.Width * image.Bitmap.PixelSize.Height * 4 +
                (image.Encoded?.Length ?? 0));
            var images = new Dictionary<string, ScreenshotAnnotationImage>(StringComparer.Ordinal);
            var layers = new List<ScreenshotAnnotation>(document.Layers.Length);
            try
            {
                foreach (var item in document.Layers)
                {
                    var record = item!;
                    ScreenshotAnnotationImage? image = null;
                    if (record.ImageId is { } id && !images.TryGetValue(id, out image))
                    {
                        if (existingImages.TryGetValue(id, out var existing))
                        {
                            image = existing.Retain();
                        }
                        else
                        {
                            if (!document.Images.TryGetValue(id, out var encoded))
                                throw new InvalidDataException("Missing annotation image resource.");
                            retainedBytes += ValidateWatermarkImage(encoded, retainedBytes);
                            using var stream = new MemoryStream(encoded, false);
                            image = new(new(stream), encoded) { Id = id };
                        }

                        images.Add(id, image);
                    }

                    var layer = new ScreenshotAnnotation
                    {
                        Tool = record.Tool, Color = record.Color, Thickness = record.Thickness,
                        Text = record.Text, FontSize = record.FontSize, MosaicSize = record.MosaicSize,
                        Properties = record.Properties with { },
                        MagnifierOffset = new(record.MagnifierOffset.X, record.MagnifierOffset.Y),
                        AutoMosaicId = record.AutoMosaicId,
                        AutoMosaicRegions = [.. record.AutoMosaicRegions.Select(region => region.Bounds)],
                        AutoMosaicCandidates = [.. record.AutoMosaicCandidates.Select(candidate => candidate!)],
                        AutoMosaicCandidateBounds = record.AutoMosaicCandidateBounds.Bounds,
                        WatermarkImage = image?.Retain(), WatermarkTimestamp = record.WatermarkTimestamp,
                    };
                    layer.Points.AddRange(record.Points.Select(point => point.Point));
                    layers.Add(layer);
                }
            }
            catch
            {
                foreach (var layer in layers) layer.Dispose();
                throw;
            }
            finally
            {
                foreach (var image in images.Values) image.Dispose();
            }

            try
            {
                CancelWork();
            }
            catch
            {
                foreach (var layer in layers) layer.Dispose();
                throw;
            }

            _pixelPreviewWork?.Cancel();
            foreach (var layer in _retained) layer.Dispose();
            _retained.Clear();
            _retained.UnionWith(layers);
            _annotations.Clear();
            _annotations.AddRange(document.Current.Select(index => layers[index]));
            RestoreDocumentHistory(_undo, document.Undo, layers);
            RestoreDocumentHistory(_redo, document.Redo, layers);
            _selected = null;
            _pixelPreviewState = [];
            _pixelPreviewFrames = [];
            AnnotationsVisible = document.Visible;
            _automaticWatermarkAttempted = true;
            SynchronizeNumberCounter();
            ClearImagePreviewCache();
            Logger.ZLogDebug(
                $"Annotation document restored. SessionId={_logSessionId} Objects={layers.Count} UndoStates={_undo.Count} RedoStates={_redo.Count}");
            Changed?.Invoke();
        }

        private static void RestoreDocumentHistory(Stack<ScreenshotAnnotation[]> history, int[]?[] states,
            List<ScreenshotAnnotation> layers)
        {
            history.Clear();
            foreach (var state in states.Reverse()) history.Push([.. state!.Select(index => layers[index])]);
        }

        internal static void ValidateDocument(ScreenshotAnnotationDocument document)
        {
            var count = document.Layers.Length;
            if (document.Current.Any(index => index < 0 || index >= count) ||
                document.Undo.Concat(document.Redo).Any(state =>
                    state is null || state.Any(index => index < 0 || index >= count)))
                throw new InvalidDataException("Invalid annotation document history.");
            foreach (var layer in document.Layers)
                if (layer is null || !Enum.IsDefined(layer.Tool) || layer.Tool == ScreenshotTool.AutoMosaic ||
                    !double.IsFinite(layer.Thickness) || layer.Thickness <= 0 ||
                    !double.IsFinite(layer.FontSize) || layer.FontSize <= 0 || layer.MosaicSize <= 0 ||
                    layer.Points.Length == 0 || layer.Points.Any(point => !point.IsValid) ||
                    layer.Properties != layer.Properties.Normalize() ||
                    !layer.MagnifierOffset.IsValid || layer.AutoMosaicRegions.Any(region => !region.IsValid) ||
                    layer.AutoMosaicCandidates.Any(candidate => candidate is not { Bounds.IsEmpty: false } ||
                                                                !Enum.IsDefined(candidate.Kind)) ||
                    !layer.AutoMosaicCandidateBounds.IsValid ||
                    (layer.ImageId is { } id && !Guid.TryParseExact(id, "N", out _)))
                    throw new InvalidDataException("Invalid annotation document layer.");
        }
    }
}

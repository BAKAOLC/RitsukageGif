using System.Buffers.Binary;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private List<ScreenshotAnnotation> CaptureHighPrecisionAnnotations(bool includeDraft)
        {
            var annotations = AnnotationsVisible
                ? _annotations.Select(annotation => CloneAnnotation(annotation)).ToList()
                : [];
            if (includeDraft && AnnotationsVisible && _gesture is { } draft) annotations.Add(CloneAnnotation(draft));
            return annotations;
        }

        public async Task<byte[]> ComposeHighPrecisionPreviewAsync(PixelRect originalRegion, PngRaster original,
            PixelRect previewRegion, CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            if (original.Width != originalRegion.Width || original.Height != originalRegion.Height ||
                previewRegion.IsEmpty || previewRegion.Intersect(originalRegion) != previewRegion)
                throw new ArgumentOutOfRangeException(nameof(previewRegion));
            var source = Png16PixelProcessor.ExpandRgba(original, token);
            var annotations = CaptureHighPrecisionAnnotations(true);
            try
            {
                var sampling = previewRegion;
                foreach (var annotation in annotations)
                {
                    var rectangle = annotation.Bounds;
                    var area = new PixelRect((int)Math.Floor(rectangle.X), (int)Math.Floor(rectangle.Y),
                            Math.Max(1, (int)Math.Ceiling(rectangle.Right) - (int)Math.Floor(rectangle.X)),
                            Math.Max(1, (int)Math.Ceiling(rectangle.Bottom) - (int)Math.Floor(rectangle.Y)))
                        .Intersect(originalRegion).Intersect(AnnotationBounds);
                    if (area.Intersect(previewRegion).IsEmpty) continue;
                    // ReSharper disable once SwitchExpressionHandlesSomeKnownEnumValuesWithDefault
                    sampling = annotation.Tool switch
                    {
                        ScreenshotTool.Mosaic or ScreenshotTool.Magnifier => Union(sampling, area),
                        ScreenshotTool.Blur => Union(sampling, ExpandSampleRegion(area.Intersect(previewRegion),
                            originalRegion,
                            annotation.Properties.BlurRadius)),
                        _ => sampling,
                    };
                }

                return CanComposeHighPrecisionOffThread(annotations)
                    ? await Task.Run(ComposeVisibleRegionAsync, token)
                    : await ComposeVisibleRegionAsync();

                async Task<byte[]> ComposeVisibleRegionAsync()
                {
                    var sampled = await Task.Run(() => Png16PixelProcessor.CropRgba(source,
                        new(sampling.X - originalRegion.X, sampling.Y - originalRegion.Y, sampling.Width,
                            sampling.Height), token), token);
                    var composed = await ComposeHighPrecisionRasterAsync(sampling, sampled, true, token, annotations);
                    var visible = Png16PixelProcessor.CropRgba(composed,
                        new(previewRegion.X - sampling.X, previewRegion.Y - sampling.Y, previewRegion.Width,
                            previewRegion.Height), token);
                    return await EncodeHighPrecisionPreviewAsync(visible, token);
                }
            }
            finally
            {
                foreach (var annotation in annotations) annotation.Dispose();
            }
        }

        private static bool CanComposeHighPrecisionOffThread(IReadOnlyList<ScreenshotAnnotation> annotations)
        {
            return annotations.All(annotation =>
                annotation.Tool is ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.Eraser ||
                (annotation is { Tool: ScreenshotTool.Watermark, WatermarkImage.Encoded: { } encoded } &&
                 IsHighPrecisionPng(encoded)));
        }

        private static PixelRect Union(PixelRect first, PixelRect second)
        {
            var x = Math.Min(first.X, second.X);
            var y = Math.Min(first.Y, second.Y);
            return new(x, y, Math.Max(first.Right, second.Right) - x, Math.Max(first.Bottom, second.Bottom) - y);
        }

        internal static Task<byte[]> EncodeHighPrecisionPreviewAsync(PngRaster result, CancellationToken token)
        {
            return Task.Run(() =>
            {
                var srgb = PngColorTransform.IsSrgbEncoded(result.Metadata)
                    ? result.Samples
                    : PngColorTransform.ConvertToSrgb(result.Samples, result.Width, result.Height,
                        16, 6, result.Metadata, token);
                var pixels = new byte[checked(result.Width * result.Height * 4)];
                for (var y = 0; y < result.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var x = 0; x < result.Width; x++)
                    {
                        var offset = (y * result.Width + x) * 8;
                        var target = (y * result.Width + x) * 4;
                        for (var channel = 0; channel < 3; channel++)
                            pixels[target + channel] = (byte)((BinaryPrimitives.ReadUInt16BigEndian(
                                srgb.AsSpan(offset + (2 - channel) * 2)) + 128) / 257);
                        pixels[target + 3] =
                            (byte)((BinaryPrimitives.ReadUInt16BigEndian(srgb.AsSpan(offset + 6)) + 128) / 257);
                    }
                }

                return pixels;
            }, token);
        }
    }
}

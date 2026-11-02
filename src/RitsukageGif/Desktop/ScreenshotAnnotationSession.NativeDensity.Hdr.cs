namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        public async Task<CapturedFrame> ComposeNativeHdrAsync(PixelRect logicalRegion, CapturedFrame source,
            CapturedFrame? sampleSource = null, PixelRect? sampleGlobalBounds = null, CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            HdrAnnotationBlender.Validate(source);
            var layout = NativeDensityLayout(logicalRegion, source);
            var sampleBounds = sampleSource is null
                ? logicalRegion
                : sampleGlobalBounds ??
                  throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(sampleGlobalBounds));
            var samples = sampleSource ?? source;
            HdrAnnotationBlender.Validate(samples);
            var sampleRegion = NativeDensitySampleRegion(layout, logicalRegion, sampleBounds);
            CommitTextEdit();
            var annotations = CaptureHighPrecisionAnnotations(true);
            try
            {
                if (annotations.Count == 0) return source;
                var needsSamples = annotations.Any(annotation => annotation is { Tool: ScreenshotTool.Eraser } or
                    { Tool: ScreenshotTool.Magnifier, Properties.MagnifierShowAnnotations: false });
                if (needsSamples && (samples.Width != sampleRegion.Width || samples.Height != sampleRegion.Height))
                    samples = await Task.Run(() => ScreenshotOutputTransform.ResizeHdr(samples,
                        sampleRegion.Width, sampleRegion.Height, token), token);
                var output = await Task.Run(() => source.Pixels[..checked(source.Stride * source.Height)].ToArray(),
                    token);
                using var pixels = ImagePreviewPixelSession(layout);
                foreach (var annotation in annotations)
                {
                    token.ThrowIfCancellationRequested();
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    using var scaled = ImagePreviewPixelAnnotation(annotation, layout);
                    // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                    switch (annotation.Tool)
                    {
                        case ScreenshotTool.Highlight:
                            await pixels.ApplyHdrMarkerAsync(output, source, layout.WorkBounds, scaled, token);
                            continue;
                        case ScreenshotTool.Mosaic:
                        case ScreenshotTool.Blur:
                            await Task.Run(
                                () => pixels.ApplyPixelLayerHdr(output, source, layout.WorkBounds, scaled, token),
                                token);
                            continue;
                        case ScreenshotTool.SmartErase:
                            await Task.Run(() => pixels.ApplyInpaintingLayerHdr(output, source, layout.WorkBounds,
                                scaled, scaled.Properties.InpaintingRadius, token), token);
                            continue;
                        case ScreenshotTool.Eraser:
                        case ScreenshotTool.Magnifier:
                            await Task.Run(() => pixels.RestoreHdrSamples(output, source, layout.WorkBounds,
                                samples, sampleRegion, scaled, token), token);
                            if (annotation.Tool == ScreenshotTool.Eraser) continue;
                            break;
                    }

                    await NativeDensityLayersAsync(annotation, layout, async (area, overlay) =>
                    {
                        await Task.Run(() => HdrAnnotationBlender.Blend(output, source, area, overlay,
                            area.Width * 4, CaptureAlphaMode.Straight, token), token);
                    }, token);
                }

                token.ThrowIfCancellationRequested();
                return source with { Pixels = output };
            }
            finally
            {
                foreach (var annotation in annotations) annotation.Dispose();
            }
        }
    }
}

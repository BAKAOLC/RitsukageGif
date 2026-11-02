using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        public async Task<CapturedFrame> ComposeImageHdrPreviewAsync(CapturedFrame original, Rect visibleSourceBounds,
            PixelSize targetPixels, CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            HdrAnnotationBlender.Validate(original);
            var annotations = CaptureHighPrecisionAnnotations(true);
            try
            {
                var layout = ImagePreviewLayout(visibleSourceBounds, targetPixels, original.Width, original.Height,
                    annotations);
                var samples = await ImagePreviewHdrSamplesAsync(original, layout, token);
                var output =
                    await Task.Run(() => samples.Pixels.Span[..checked(samples.Stride * samples.Height)].ToArray(),
                        token);
                using var pixels = ImagePreviewPixelSession(layout);
                foreach (var annotation in annotations)
                {
                    token.ThrowIfCancellationRequested();
                    using var scaled = ImagePreviewPixelAnnotation(annotation, layout);
                    // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                    switch (annotation.Tool)
                    {
                        case ScreenshotTool.Highlight:
                            await pixels.ApplyHdrMarkerAsync(output, samples, layout.WorkBounds, scaled, token);
                            continue;
                        case ScreenshotTool.Mosaic:
                        case ScreenshotTool.Blur:
                            await Task.Run(
                                () => pixels.ApplyPixelLayerHdr(output, samples, layout.WorkBounds, scaled, token),
                                token);
                            continue;
                        case ScreenshotTool.SmartErase:
                            await Task.Run(() => pixels.ApplyInpaintingLayerHdr(output, samples, layout.WorkBounds,
                                scaled,
                                scaled.Properties.InpaintingRadius, token), token);
                            continue;
                        case ScreenshotTool.Eraser:
                        case ScreenshotTool.Magnifier:
                            await Task.Run(() => pixels.RestoreHdrSamples(output, samples, layout.WorkBounds,
                                samples, layout.WorkBounds, scaled, token), token);
                            if (annotation.Tool == ScreenshotTool.Eraser) continue;
                            break;
                    }

                    if (ImagePreviewOverlay(annotation, layout, token) is not { } layer) continue;
                    await Task.Run(() => HdrAnnotationBlender.Blend(output, samples, layer.Area, layer.Pixels,
                        layer.Area.Width * 4, CaptureAlphaMode.Straight, token), token);
                }

                var composed = samples with { Pixels = output };
                return await Task.Run(() => ScreenshotOutputTransform.ResizeHdr(composed, targetPixels.Width,
                    targetPixels.Height, token, layout.VisibleInWork), token);
            }
            finally
            {
                foreach (var annotation in annotations) annotation.Dispose();
            }
        }

        private async Task<CapturedFrame> ImagePreviewHdrSamplesAsync(CapturedFrame original,
            ImagePreviewLayout layout, CancellationToken token)
        {
            if (ReferenceEquals(_imagePreviewHdrSource, original) && _imagePreviewHdrLayout is { } cached &&
                cached.Sampling == layout.Sampling && cached.WorkSize == layout.WorkSize &&
                _imagePreviewHdrSample is { } sample)
                return sample;
            var sampled = await Task.Run(() => ScreenshotOutputTransform.ResizeHdr(original, layout.WorkSize.Width,
                layout.WorkSize.Height, token, new(layout.Sampling.X, layout.Sampling.Y,
                    layout.Sampling.Width, layout.Sampling.Height)), token);
            token.ThrowIfCancellationRequested();
            _imagePreviewHdrSource = original;
            _imagePreviewHdrLayout = layout;
            return _imagePreviewHdrSample = sampled;
        }
    }
}

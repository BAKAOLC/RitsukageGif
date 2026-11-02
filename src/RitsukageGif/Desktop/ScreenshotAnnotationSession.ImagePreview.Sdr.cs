using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        public async Task<byte[]> ComposeImageSdrPreviewAsync(byte[] originalPng, byte[] originalBgra,
            Rect visibleSourceBounds, PixelSize targetPixels, CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var dimensions = PngImageCodec.ReadDimensions(originalPng);
            var annotations = CaptureHighPrecisionAnnotations(true);
            try
            {
                var layout = ImagePreviewLayout(visibleSourceBounds, targetPixels, dimensions.Width, dimensions.Height,
                    annotations);
                var samples = await ImagePreviewSdrSamplesAsync(originalPng, originalBgra, dimensions.Width,
                    dimensions.Height, layout, token);
                var output = await Task.Run(() => samples.Samples.ToArray(), token);
                using var pixels = ImagePreviewPixelSession(layout);
                foreach (var annotation in annotations)
                {
                    token.ThrowIfCancellationRequested();
                    using var scaled = ImagePreviewPixelAnnotation(annotation, layout);
                    // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                    switch (annotation.Tool)
                    {
                        case ScreenshotTool.Highlight:
                            await pixels.ApplyHighPrecisionMarkerAsync(output, samples, layout.WorkBounds, scaled,
                                token);
                            continue;
                        case ScreenshotTool.Mosaic:
                        case ScreenshotTool.Blur:
                            await Task.Run(
                                () => pixels.ApplyPixelLayer16(output, samples, layout.WorkBounds, scaled, token),
                                token);
                            continue;
                        case ScreenshotTool.SmartErase:
                            await Task.Run(() => pixels.ApplyInpaintingLayer16(output, samples, layout.WorkBounds,
                                scaled,
                                scaled.Properties.InpaintingRadius, token), token);
                            continue;
                        case ScreenshotTool.Magnifier:
                            await Task.Run(
                                () => pixels.MagnifyHighPrecisionLayer(output, samples, layout.WorkBounds, scaled,
                                    token), token);
                            break;
                        case ScreenshotTool.Eraser:
                            await pixels.RestoreHighPrecisionAsync(output, samples, layout.WorkBounds, scaled, token);
                            continue;
                    }

                    if (ImagePreviewOverlay(annotation, layout, token) is not { } layer) continue;
                    await Task.Run(() =>
                    {
                        var foreground = PngColorTransform.OverlayToSource(layer.Pixels, layer.Area.Width,
                            layer.Area.Height, samples.Metadata, token);
                        Png16PixelProcessor.Blend(output, samples.Width, samples.Height, layer.Area, foreground, token);
                    }, token);
                }

                var visible = await Task.Run(() => ImagePreviewResampler.SampleSdr(output, samples.Width,
                    samples.Height, true, layout.VisibleInWork, targetPixels, samples.Metadata, token), token);
                return await EncodeHighPrecisionPreviewAsync(visible, token);
            }
            finally
            {
                foreach (var annotation in annotations) annotation.Dispose();
            }
        }

        private async Task<PngRaster> ImagePreviewSdrSamplesAsync(byte[] originalPng, byte[] originalBgra,
            int width, int height, ImagePreviewLayout layout, CancellationToken token)
        {
            var sixteen = IsHighPrecisionPng(originalPng);
            object identity = sixteen ? originalPng : originalBgra;
            if (ReferenceEquals(_imagePreviewSdrSource, identity) && _imagePreviewSdrLayout is { } cached &&
                cached.Sampling == layout.Sampling && cached.WorkSize == layout.WorkSize &&
                _imagePreviewSdrSample is { } sample)
                return sample;
            if (sixteen && !ReferenceEquals(_imagePreviewPng, originalPng))
            {
                var decoded = await Task.Run(() => PngColorTransform.NormalizeAnnotationRaster(
                    PngImageCodec.Decode(originalPng, token), token), token);
                token.ThrowIfCancellationRequested();
                _imagePreviewPng = originalPng;
                _imagePreviewPngRaster = decoded;
            }

            var metadata = sixteen
                ? _imagePreviewPngRaster!.Metadata
                : [("sRGB", new byte[] { 0 })];
            ReadOnlyMemory<byte> source = sixteen ? _imagePreviewPngRaster!.Samples : originalBgra;
            var sampled = await Task.Run(() => ImagePreviewResampler.SampleSdr(source, width, height,
                sixteen, new(layout.Sampling.X, layout.Sampling.Y, layout.Sampling.Width, layout.Sampling.Height),
                layout.WorkSize, metadata, token), token);
            token.ThrowIfCancellationRequested();
            _imagePreviewSdrSource = identity;
            _imagePreviewSdrLayout = layout;
            return _imagePreviewSdrSample = sampled;
        }
    }
}

using System.Buffers.Binary;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        public async Task<CapturedFrame> ComposeNativeSdrAsync(PixelRect logicalRegion, CapturedFrame source,
            CapturedFrame? sampleSource = null, PixelRect? sampleGlobalBounds = null, CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            ValidateNativeSdr(source);
            var layout = NativeDensityLayout(logicalRegion, source);
            var sampleBounds = sampleSource is null
                ? logicalRegion
                : sampleGlobalBounds ??
                  throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(sampleGlobalBounds));
            var samples = sampleSource ?? source;
            ValidateNativeSdr(samples);
            var sampleRegion = NativeDensitySampleRegion(layout, logicalRegion, sampleBounds);
            CommitTextEdit();
            var annotations = CaptureHighPrecisionAnnotations(true);
            try
            {
                if (annotations.Count == 0) return source;
                var original = await Task.Run(() => NativeSdrRaster(source, token), token);
                var needsExternalSamples = !ReferenceEquals(samples, source) && annotations.Any(annotation =>
                    annotation is { Tool: ScreenshotTool.Magnifier, Properties.MagnifierShowAnnotations: false });
                var originalSamples = needsExternalSamples
                    ? await Task.Run(() => NativeSdrRaster(samples, token), token)
                    : original;
                if (needsExternalSamples &&
                    (originalSamples.Width != sampleRegion.Width || originalSamples.Height != sampleRegion.Height))
                    originalSamples = await Task.Run(() => ImagePreviewResampler.SampleSdr(originalSamples.Samples,
                        originalSamples.Width, originalSamples.Height, true,
                        new(0, 0, originalSamples.Width, originalSamples.Height),
                        new(sampleRegion.Width, sampleRegion.Height), originalSamples.Metadata, token), token);
                var output = original.Samples.ToArray();
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
                            await pixels.ApplyHighPrecisionMarkerAsync(output, original, layout.WorkBounds, scaled,
                                token);
                            continue;
                        case ScreenshotTool.Mosaic:
                        case ScreenshotTool.Blur:
                            await Task.Run(
                                () => pixels.ApplyPixelLayer16(output, original, layout.WorkBounds, scaled, token),
                                token);
                            continue;
                        case ScreenshotTool.SmartErase:
                            await Task.Run(() => pixels.ApplyInpaintingLayer16(output, original, layout.WorkBounds,
                                scaled, scaled.Properties.InpaintingRadius, token), token);
                            continue;
                        case ScreenshotTool.Eraser:
                            await pixels.RestoreHighPrecisionAsync(output, original, layout.WorkBounds, scaled, token);
                            continue;
                        case ScreenshotTool.Magnifier:
                            await Task.Run(() => pixels.NativeDensityMagnifySdr(output, original, originalSamples,
                                sampleRegion, scaled, token), token);
                            break;
                    }

                    await NativeDensityLayersAsync(annotation, layout, async (area, overlay) =>
                    {
                        await Task.Run(() =>
                        {
                            var foreground = PngColorTransform.OverlayToSource(overlay, area.Width, area.Height,
                                original.Metadata, token);
                            Png16PixelProcessor.Blend(output, original.Width, original.Height, area, foreground, token);
                        }, token);
                    }, token);
                }

                var result = new byte[checked(source.Width * source.Height * 4)];
                await Task.Run(() =>
                {
                    for (var y = 0; y < source.Height; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        for (var x = 0; x < source.Width; x++)
                        {
                            var input = output.AsSpan((y * source.Width + x) * 8, 8);
                            var target = result.AsSpan((y * source.Width + x) * 4, 4);
                            for (var channel = 0; channel < 3; channel++)
                                target[2 - channel] =
                                    (byte)((BinaryPrimitives.ReadUInt16BigEndian(input[(channel * 2)..]) + 128) / 257);
                            target[3] = (byte)((BinaryPrimitives.ReadUInt16BigEndian(input[6..]) + 128) / 257);
                        }
                    }
                }, token);
                return source with
                {
                    Pixels = result, Stride = source.Width * 4,
                    AlphaMode = source.AlphaMode == CaptureAlphaMode.Opaque
                        ? CaptureAlphaMode.Opaque
                        : CaptureAlphaMode.Straight,
                };
            }
            finally
            {
                foreach (var annotation in annotations) annotation.Dispose();
            }
        }

        private static ImagePreviewLayout NativeDensityLayout(PixelRect region, CapturedFrame source)
        {
            if (region.IsEmpty || source.Width <= 0 || source.Height <= 0)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(region));
            return new(new(region.X, region.Y, region.Width, region.Height), region, new(source.Width, source.Height));
        }

        private static PixelRect NativeDensitySampleRegion(ImagePreviewLayout layout, PixelRect region,
            PixelRect samples)
        {
            if (samples.IsEmpty || samples.Intersect(region) != region)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(samples));
            var left = checked((int)Math.Round((samples.X - (double)region.X) * layout.ScaleX));
            var top = checked((int)Math.Round((samples.Y - (double)region.Y) * layout.ScaleY));
            var right = checked((int)Math.Round((samples.Right - (double)region.X) * layout.ScaleX));
            var bottom = checked((int)Math.Round((samples.Bottom - (double)region.Y) * layout.ScaleY));
            return new(left, top, checked(right - left), checked(bottom - top));
        }

        private async Task NativeDensityLayersAsync(ScreenshotAnnotation annotation, ImagePreviewLayout layout,
            Func<PixelRect, byte[], Task> blend, CancellationToken token)
        {
            var affected = RenderBounds(annotation).TransformToAABB(layout.Transform).Inflate(1);
            const int tileSize = 1024;
            for (var y = 0; y < layout.WorkSize.Height; y += tileSize)
            for (var x = 0; x < layout.WorkSize.Width; x += tileSize)
            {
                token.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                var area = new PixelRect(x, y, Math.Min(tileSize, layout.WorkSize.Width - x),
                    Math.Min(tileSize, layout.WorkSize.Height - y));
                if (!affected.Intersects(new(x, y, area.Width, area.Height))) continue;
                using var tile = new RenderTargetBitmap(new(area.Width, area.Height), new(96, 96));
                using (var drawing = tile.CreateDrawingContext())
                {
                    using var transform = drawing.PushTransform(new(layout.ScaleX, 0, 0, layout.ScaleY,
                        -layout.Sampling.X * layout.ScaleX - area.X, -layout.Sampling.Y * layout.ScaleY - area.Y));
                    using var clip = drawing.PushClip(new(AnnotationBounds.X, AnnotationBounds.Y,
                        AnnotationBounds.Width, AnnotationBounds.Height));
                    Draw(drawing, annotation, false);
                }

                using var pixels = new ImagePreviewFramebuffer(new(area.Width, area.Height));
                tile.CopyPixels(pixels);
                await blend(area, pixels.Pixels);
            }
        }

        private static void ValidateNativeSdr(CapturedFrame source)
        {
            if (source.PixelFormat != CapturePixelFormat.Bgra8Srgb || source.Width <= 0 || source.Height <= 0 ||
                source.Stride < (long)source.Width * 4 || source.Pixels.Length < (long)source.Stride * source.Height ||
                !Enum.IsDefined(source.AlphaMode))
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(source));
        }

        private static PngRaster NativeSdrRaster(CapturedFrame source, CancellationToken token)
        {
            var result = new byte[checked(source.Width * source.Height * 8)];
            for (var y = 0; y < source.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                var row = source.Pixels.Span.Slice(y * source.Stride, source.Width * 4);
                for (var x = 0; x < source.Width; x++)
                {
                    var pixel = row.Slice(x * 4, 4);
                    var alpha = source.AlphaMode == CaptureAlphaMode.Opaque ? 255 : pixel[3];
                    var target = result.AsSpan((y * source.Width + x) * 8, 8);
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var value = source.AlphaMode == CaptureAlphaMode.Premultiplied
                            ? alpha == 0 ? 0 : Math.Min(255, (pixel[2 - channel] * 255 + alpha / 2) / alpha)
                            : pixel[2 - channel];
                        BinaryPrimitives.WriteUInt16BigEndian(target[(channel * 2)..], (ushort)(value * 257));
                    }

                    BinaryPrimitives.WriteUInt16BigEndian(target[6..], (ushort)(alpha * 257));
                }
            }

            return new(source.Width, source.Height, 16, 6, result, [("sRGB", new byte[] { 0 })]);
        }

        private void NativeDensityMagnifySdr(byte[] output, PngRaster destination, PngRaster original,
            PixelRect originalRegion, ScreenshotAnnotation annotation, CancellationToken token)
        {
            var region = new PixelRect(0, 0, destination.Width, destination.Height);
            var showAnnotations = annotation.Properties.MagnifierShowAnnotations;
            var sampleRegion = MagnifierPixelBounds(annotation.Bounds)
                .Intersect(showAnnotations ? region : originalRegion);
            if (sampleRegion.IsEmpty) return;
            var samples = CropMagnifierSamples(showAnnotations ? output : original.Samples,
                showAnnotations ? region : originalRegion,
                (showAnnotations ? destination.Width : original.Width) * 8, sampleRegion, token);
            var sampling = new MagnifierPixelSource(samples, sampleRegion, sampleRegion.Width * 8, 1);
            var lens = MagnifierLensBounds(annotation);
            var area = MagnifierPixelBounds(lens).Intersect(region).Intersect(AnnotationBounds);
            Span<double> sample = stackalloc double[4];
            Span<double> behind = stackalloc double[4];
            for (var y = area.Y; y < area.Bottom; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = area.X; x < area.Right; x++)
                {
                    var coverage = MagnifierCoverage(annotation, lens, x, y);
                    if (coverage == 0) continue;
                    SampleMagnifierPixel(sampling, annotation, lens, x, y, sample);
                    var index = (y * destination.Width + x) * 8;
                    for (var channel = 0; channel < 4; channel++)
                        behind[channel] = BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(index + channel * 2)) /
                                          65535d;
                    BlendMagnifierPixel(sample, behind, coverage);
                    for (var channel = 0; channel < 4; channel++)
                        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(index + channel * 2),
                            (ushort)Math.Clamp(Math.Round(sample[channel] * 65535), 0, 65535));
                }
            }
        }
    }
}

using Avalonia;
using Avalonia.Media.Imaging;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private async Task<byte[]> ComposeHighPrecisionAsync(PixelRect region, byte[] sourcePng,
            CancellationToken token)
        {
            var source =
                await Task.Run(
                    () => PngColorTransform.NormalizeAnnotationRaster(PngImageCodec.Decode(sourcePng, token), token),
                    token);
            var result = await ComposeHighPrecisionRasterAsync(region, source, false, token);
            return await Task.Run(() => PngImageCodec.Encode(result, token), token);
        }

        private async Task<PngRaster> ComposeHighPrecisionRasterAsync(PixelRect region, PngRaster source,
            bool includeDraft, CancellationToken token, IReadOnlyList<ScreenshotAnnotation>? snapshot = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            var owned = snapshot is null ? CaptureHighPrecisionAnnotations(includeDraft) : null;
            var annotations = snapshot ?? owned!;
            try
            {
                if (source.Width != region.Width || source.Height != region.Height)
                    throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(source));
                using var colorSpace = PngColorTransform.CreateSourceSpace(source.Metadata, token);
                var output = await Task.Run(() => source.Samples.ToArray(), token);
                var watermarkSamples = new Dictionary<ScreenshotAnnotationImage, PngRaster>();
                foreach (var annotation in annotations)
                {
                    token.ThrowIfCancellationRequested();
                    var bounds = annotation.Bounds;
                    var global = new PixelRect((int)Math.Floor(bounds.X), (int)Math.Floor(bounds.Y),
                            Math.Max(1, (int)Math.Ceiling(bounds.Right) - (int)Math.Floor(bounds.X)),
                            Math.Max(1, (int)Math.Ceiling(bounds.Bottom) - (int)Math.Floor(bounds.Y)))
                        .Intersect(region).Intersect(AnnotationBounds);
                    var local = new PixelRect(global.X - region.X, global.Y - region.Y, global.Width, global.Height);
                    // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                    switch (annotation.Tool)
                    {
                        case ScreenshotTool.Mosaic:
                            await Task.Run(() => Png16PixelProcessor.Mosaic(output, source.Samples, source.Width,
                                source.Height, local, annotation.MosaicSize, token), token);
                            break;
                        case ScreenshotTool.Blur:
                            await Task.Run(() => Png16PixelProcessor.Blur(output, source.Samples, source.Width,
                                source.Height, local, annotation.Properties.BlurRadius, token), token);
                            break;
                        case ScreenshotTool.Magnifier:
                            await Task.Run(() => Png16PixelProcessor.Magnify(output, source.Samples, source.Width,
                                source.Height, local, bounds.Center.X - region.X, bounds.Center.Y - region.Y,
                                annotation.Properties.Magnification, token), token);
                            await CompositeHighPrecisionLayerAsync(output, source, region, annotation, token);
                            break;
                        case ScreenshotTool.Eraser:
                            await RestoreHighPrecisionAsync(output, source, region, annotation, token);
                            break;
                        case ScreenshotTool.Watermark when annotation.WatermarkImage?.Encoded is { } encoded &&
                                                           IsHighPrecisionPng(encoded):
                            if (!watermarkSamples.TryGetValue(annotation.WatermarkImage, out var image))
                            {
                                image = await Task.Run(() =>
                                {
                                    var decoded =
                                        PngColorTransform.NormalizeAnnotationRaster(
                                            PngImageCodec.Decode(encoded, token), token);
                                    return decoded with
                                    {
                                        Samples = PngColorTransform.ConvertSamples(decoded.Samples,
                                            decoded.Width, decoded.Height, 16, 6, decoded.Metadata, token,
                                            source.Metadata),
                                    };
                                }, token);
                                watermarkSamples.Add(annotation.WatermarkImage, image);
                            }

                            await DrawHighPrecisionWatermarkAsync(output, source, region, annotation, image, token);
                            break;
                        default:
                            await CompositeHighPrecisionLayerAsync(output, source, region, annotation, token);
                            break;
                    }
                }

                return source with
                {
                    Samples = output,
                    Metadata =
                    [
                        .. source.Metadata.Select(chunk => chunk.Type == "sBIT"
                            ? (chunk.Type, (ReadOnlyMemory<byte>)(byte[])[16, 16, 16, 16])
                            : chunk),
                    ],
                };
            }
            finally
            {
                if (owned is not null)
                    foreach (var annotation in owned)
                        annotation.Dispose();
            }
        }

        private static bool IsHighPrecisionPng(byte[] encoded)
        {
            return encoded.Length >= 33 && encoded[24] == 16 &&
                   encoded.AsSpan(0, 8).SequenceEqual((ReadOnlySpan<byte>)[137, 80, 78, 71, 13, 10, 26, 10]);
        }

        private async Task DrawHighPrecisionWatermarkAsync(byte[] output, PngRaster source, PixelRect region,
            ScreenshotAnnotation annotation, PngRaster image, CancellationToken token)
        {
            var width = annotation.Properties.ImageWidth;
            var height = width * image.Height / image.Width;
            if (annotation.Properties.Tiled)
            {
                for (var top = (double)AnnotationBounds.Y;
                     top < AnnotationBounds.Bottom;
                     top += Math.Max(16, height * 1.5))
                for (var left = (double)AnnotationBounds.X;
                     left < AnnotationBounds.Right;
                     left += Math.Max(16, width * 1.5))
                    await DrawAt(left, top);
            }
            else
            {
                var origin = annotation.Properties.NineGrid
                    ? WatermarkOrigin(new(width, height), annotation.Properties.Anchor)
                    : annotation.Points[0];
                await DrawAt(origin.X, origin.Y);
            }

            return;

            async Task DrawAt(double left, double top)
            {
                var rectangle = new PixelRect((int)Math.Floor(left), (int)Math.Floor(top),
                        Math.Max(1, (int)Math.Ceiling(left + width) - (int)Math.Floor(left)),
                        Math.Max(1, (int)Math.Ceiling(top + height) - (int)Math.Floor(top)))
                    .Intersect(region).Intersect(AnnotationBounds);
                const int tileSize = 512;
                for (var y = rectangle.Y; y < rectangle.Bottom; y += tileSize)
                for (var x = rectangle.X; x < rectangle.Right; x += tileSize)
                {
                    var area = new PixelRect(x, y, Math.Min(tileSize, rectangle.Right - x),
                        Math.Min(tileSize, rectangle.Bottom - y));
                    await Task.Run(() =>
                    {
                        var sampled = Png16PixelProcessor.SampleScaled(image.Samples, image.Width, image.Height,
                            area, left, top, width, height, annotation.Properties.Opacity, token);
                        Png16PixelProcessor.Blend(output, source.Width, source.Height,
                            new(area.X - region.X, area.Y - region.Y, area.Width, area.Height), sampled, token);
                    }, token);
                }
            }
        }

        private async Task CompositeHighPrecisionLayerAsync(byte[] output, PngRaster source, PixelRect region,
            ScreenshotAnnotation annotation, CancellationToken token)
        {
            var bounds = RenderBounds(annotation);
            const int tileSize = 512;
            for (var top = 0; top < source.Height; top += tileSize)
            for (var left = 0; left < source.Width; left += tileSize)
            {
                token.ThrowIfCancellationRequested();
                var width = Math.Min(tileSize, source.Width - left);
                var height = Math.Min(tileSize, source.Height - top);
                var world = new Rect(region.X + left, region.Y + top, width, height);
                if (!bounds.Intersects(world)) continue;
                using var tile = new RenderTargetBitmap(new(width, height), new(96, 96));
                using (var context = tile.CreateDrawingContext())
                {
                    using var transform = context.PushTransform(Matrix.CreateTranslation(-world.X, -world.Y));
                    using var clip = context.PushClip(new(AnnotationBounds.X, AnnotationBounds.Y,
                        AnnotationBounds.Width, AnnotationBounds.Height));
                    Draw(context, annotation, false);
                }

                var x = left;
                var y = top;
                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    using var stream = new MemoryStream();
                    tile.Save(stream);
                    using var overlay = new MagickImage(stream.ToArray());
                    using var pixels = overlay.GetPixels();
                    var bgra = pixels.ToByteArray(PixelMapping.BGRA)!;
                    var foreground = PngColorTransform.OverlayToSource(bgra, width, height, source.Metadata, token);
                    Png16PixelProcessor.Blend(output, source.Width, source.Height, new(x, y, width, height), foreground,
                        token);
                }, token);
            }
        }

        private Task RestoreHighPrecisionAsync(byte[] output, PngRaster source, PixelRect region,
            ScreenshotAnnotation annotation, CancellationToken token)
        {
            return Task.Run(() =>
            {
                var bounds = RenderBounds(annotation);
                var erased = new PixelRect((int)Math.Floor(bounds.X), (int)Math.Floor(bounds.Y),
                        (int)Math.Ceiling(bounds.Right) - (int)Math.Floor(bounds.X),
                        (int)Math.Ceiling(bounds.Bottom) - (int)Math.Floor(bounds.Y)).Intersect(region)
                    .Intersect(AnnotationBounds);
                const int tileSize = 512;
                for (var top = erased.Y; top < erased.Bottom; top += tileSize)
                for (var left = erased.X; left < erased.Right; left += tileSize)
                {
                    token.ThrowIfCancellationRequested();
                    var world = new PixelRect(left, top, Math.Min(tileSize, erased.Right - left),
                        Math.Min(tileSize, erased.Bottom - top));
                    var coverage = CreateEraserCoverage(annotation, world, token);
                    Png16PixelProcessor.Restore(output, source.Samples, source.Width, source.Height,
                        new(world.X - region.X, world.Y - region.Y, world.Width, world.Height), coverage, token);
                }
            }, token);
        }
    }
}

using Avalonia;
using Avalonia.Media.Imaging;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        public Task<CapturedFrame> ComposeHdrAsync(PixelRect globalRegion, CapturedFrame source,
            CapturedFrame? sampleSource = null, PixelRect? sampleGlobalBounds = null, CancellationToken token = default)
        {
            return LogCompositionAsync(
                ComposeHdrCoreAsync(globalRegion, source, sampleSource, sampleGlobalBounds, token),
                globalRegion, 16, true);
        }

        private async Task<CapturedFrame> ComposeHdrCoreAsync(PixelRect globalRegion, CapturedFrame source,
            CapturedFrame? sampleSource, PixelRect? sampleGlobalBounds, CancellationToken token,
            IReadOnlyList<ScreenshotAnnotation>? snapshot = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            HdrAnnotationBlender.Validate(source);
            if (globalRegion.Width != source.Width || globalRegion.Height != source.Height || globalRegion.IsEmpty)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(globalRegion));
            var samples = sampleSource ?? source;
            var sampleRegion = sampleSource is null
                ? globalRegion
                : sampleGlobalBounds ??
                  throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(sampleGlobalBounds));
            HdrAnnotationBlender.Validate(samples);
            if (samples.Width != sampleRegion.Width || samples.Height != sampleRegion.Height ||
                sampleRegion.Intersect(globalRegion) != globalRegion)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(sampleSource));
            token.ThrowIfCancellationRequested();
            if (snapshot is null) CommitTextEdit();
            var annotations = snapshot ?? [.. _annotations];
            if (annotations.Count == 0 || !AnnotationsVisible) return source;
            var output = await Task.Run(() => source.Pixels[..checked(source.Stride * source.Height)].ToArray(), token);
            foreach (var annotation in annotations)
            {
                token.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                switch (annotation.Tool)
                {
                    case ScreenshotTool.Highlight:
                        await ApplyHdrMarkerAsync(output, source, globalRegion, annotation, token);
                        continue;
                    case ScreenshotTool.Magnifier:
                    case ScreenshotTool.Eraser:
                        await Task.Run(() => RestoreHdrSamples(output, source, globalRegion, samples, sampleRegion,
                            annotation, token), token);
                        if (annotation.Tool == ScreenshotTool.Eraser) continue;
                        break;
                    case ScreenshotTool.Mosaic:
                    case ScreenshotTool.Blur:
                        await Task.Run(() => ApplyPixelLayerHdr(output, source, globalRegion, annotation, token),
                            token);
                        continue;
                    case ScreenshotTool.SmartErase:
                        await Task.Run(() => ApplyInpaintingLayerHdr(output, source, globalRegion, annotation,
                            annotation.Properties.InpaintingRadius, token), token);
                        continue;
                }

                var area = RenderBounds(annotation);
                const int tileSize = 1024;
                for (var y = 0; y < source.Height; y += tileSize)
                for (var x = 0; x < source.Width; x += tileSize)
                {
                    token.ThrowIfCancellationRequested();
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    var width = Math.Min(tileSize, source.Width - x);
                    var height = Math.Min(tileSize, source.Height - y);
                    var world = new Rect(globalRegion.X + x, globalRegion.Y + y, width, height);
                    if (!area.Intersects(world)) continue;
                    using var tile = new RenderTargetBitmap(new(width, height), new(96, 96));
                    using (var context = tile.CreateDrawingContext())
                    {
                        using var translate = context.PushTransform(Matrix.CreateTranslation(-world.X, -world.Y));
                        using var clip =
                            context.PushClip(new(Selection.X, Selection.Y, Selection.Width, Selection.Height));
                        Draw(context, annotation, false);
                    }

                    var local = new PixelRect(x, y, width, height);
                    await Task.Run(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        using var stream = new MemoryStream();
                        tile.Save(stream, PngBitmapEncoderOptions.Default);
                        using var overlay = new MagickImage(stream.ToArray());
                        using var pixels = overlay.GetPixels();
                        var bgra = pixels.ToByteArray(PixelMapping.BGRA)!;
                        HdrAnnotationBlender.Blend(output, source, local, bgra, width * 4, CaptureAlphaMode.Straight,
                            token);
                    }, token);
                }
            }

            token.ThrowIfCancellationRequested();
            return source with { Pixels = output };
        }
    }
}

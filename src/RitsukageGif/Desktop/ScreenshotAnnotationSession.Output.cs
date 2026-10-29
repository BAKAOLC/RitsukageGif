using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        public event Action<bool?>? StartScrollingRequested;

        public void RequestScrolling(bool? vertical = null)
        {
            if (CanStartScrolling) StartScrollingRequested?.Invoke(vertical);
        }

        private async Task<IReadOnlyList<(Bitmap Image, Rect Bounds)>> CreateMosaicAsync(Rect rectangle, int block,
            CancellationToken token, (PixelRect Region, byte[] Pixels)[]? sourceFrames = null, int blurRadius = 0)
        {
            var region = new PixelRect((int)Math.Floor(rectangle.X), (int)Math.Floor(rectangle.Y),
                Math.Max(1, (int)Math.Ceiling(rectangle.Right) - (int)Math.Floor(rectangle.X)),
                Math.Max(1, (int)Math.Ceiling(rectangle.Bottom) - (int)Math.Floor(rectangle.Y)));
            if (sourceFrames is null) region = region.Intersect(AnnotationBounds);
            if (region.IsEmpty || (long)region.Width * region.Height > 16L * 1024 * 1024)
                throw new InvalidOperationException(UiText.Get("RecordingTooLarge"));
            var retainedBytes = _retained.SelectMany(annotation => annotation.ImageTiles)
                .Sum(tile => (long)tile.Image.PixelSize.Width * tile.Image.PixelSize.Height * 4);
            if ((sourceFrames is null ? retainedBytes : 0) + (long)region.Width * region.Height * 4 >
                128L * 1024 * 1024)
                throw new InvalidOperationException(UiText.Get("RecordingTooLarge"));
            var frames = sourceFrames ??
                         (_overlayFrame is { } overlay ? [.. _frames, overlay] : [.. _frames]);
            var samplingRegion = blurRadius == 0 ? region : ExpandSampleRegion(region, AnnotationBounds, blurRadius);
            if ((long)samplingRegion.Width * samplingRegion.Height > 20L * 1024 * 1024)
                throw new InvalidOperationException(UiText.Get("RecordingTooLarge"));
            var pixels = await Task.Run(() =>
            {
                var result = new byte[checked(samplingRegion.Width * samplingRegion.Height * 4)];
                foreach (var frame in frames)
                {
                    var intersection = frame.Region.Intersect(samplingRegion);
                    if (intersection.IsEmpty) continue;
                    for (var y = intersection.Y; y < intersection.Bottom; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        Buffer.BlockCopy(frame.Pixels,
                            ((y - frame.Region.Y) * frame.Region.Width + intersection.X - frame.Region.X) * 4,
                            result,
                            ((y - samplingRegion.Y) * samplingRegion.Width + intersection.X - samplingRegion.X) * 4,
                            intersection.Width * 4);
                    }
                }

                if (blurRadius > 0)
                    PixelEffectProcessor.Apply(result, samplingRegion.Width, samplingRegion.Height,
                        [new(PixelEffectKind.Blur, Radius: blurRadius)], token: token);
                else if (block > 1)
                    PixelEffectProcessor.Apply(result, region.Width, region.Height,
                        [new(PixelEffectKind.Mosaic, BlockSize: block)], token: token);
                return result;
            }, token);
            token.ThrowIfCancellationRequested();
            var tiles = new List<(Bitmap Image, Rect Bounds)>();
            try
            {
                for (var top = 0; top < region.Height; top += 1024)
                for (var left = 0; left < region.Width; left += 1024)
                {
                    token.ThrowIfCancellationRequested();
                    var width = Math.Min(1024, region.Width - left);
                    var height = Math.Min(1024, region.Height - top);
                    var bitmap = new WriteableBitmap(new(width, height), new(96, 96), PixelFormat.Bgra8888,
                        AlphaFormat.Unpremul);
                    tiles.Add((bitmap, new(region.X + left, region.Y + top, width, height)));
                    using var buffer = bitmap.Lock();
                    for (var y = 0; y < height; y++)
                        Marshal.Copy(pixels, ((top + y + region.Y - samplingRegion.Y) * samplingRegion.Width +
                                left + region.X - samplingRegion.X) * 4,
                            buffer.Address + y * buffer.RowBytes, width * 4);
                }

                return tiles;
            }
            catch
            {
                foreach (var tile in tiles) tile.Image.Dispose();
                throw;
            }
        }

        private static PixelRect ExpandSampleRegion(PixelRect region, PixelRect bounds, int radius)
        {
            var left = (int)Math.Max(bounds.X, (long)region.X - radius);
            var top = (int)Math.Max(bounds.Y, (long)region.Y - radius);
            var right = (int)Math.Min(bounds.Right, (long)region.Right + radius);
            var bottom = (int)Math.Min(bounds.Bottom, (long)region.Bottom + radius);
            return new(left, top, right - left, bottom - top);
        }

        public Task<byte[]> ComposeAsync(PixelRect region, byte[] sourcePng, CancellationToken token = default)
        {
            return LogCompositionAsync(ComposePngCoreAsync(region, sourcePng, token), region,
                sourcePng.Length >= 33 && sourcePng[24] == 16 ? 16 : 8, false);
        }

        private Task<byte[]> ComposePngCoreAsync(PixelRect region, byte[] sourcePng, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (region.IsEmpty) throw new ArgumentOutOfRangeException(nameof(region));
            CommitTextEdit();
            if ((!HasAnnotations || !AnnotationsVisible) && !IsDrawing)
                return Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var info = new MagickImageInfo(sourcePng);
                    if (info.Width != region.Width || info.Height != region.Height)
                        throw new InvalidOperationException(UiText.Get("InvalidFrameBuffer"));
                    token.ThrowIfCancellationRequested();
                    return sourcePng;
                }, token);
            if (sourcePng.Length >= 33 && sourcePng[24] == 16)
                return ComposeHighPrecisionAsync(region, sourcePng, token);
            return ComposeImageAsync(region, cancellation =>
            {
                var image = new MagickImage();
                try
                {
                    image.Progress += (_, progress) => progress.Cancel = cancellation.IsCancellationRequested;
                    image.Read(sourcePng);
                    return image;
                }
                catch
                {
                    image.Dispose();
                    throw;
                }
            }, token);
        }

        public Task<byte[]> ComposePreviewAsync(PixelRect region, byte[] sourceBgra, CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            if (region.IsEmpty || (long)region.Width * region.Height * 4 != sourceBgra.Length)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(sourceBgra));
            if ((!HasAnnotations || !AnnotationsVisible) && !IsDrawing) return Task.FromResult(sourceBgra);
            return ComposeImageAsync(region, cancellation =>
            {
                cancellation.ThrowIfCancellationRequested();
                return new(sourceBgra, new PixelReadSettings((uint)region.Width, (uint)region.Height,
                    StorageType.Char, PixelMapping.BGRA));
            }, token, true);
        }

        public Task<byte[]> ComposeAsync(PixelRect region, ScrollCaptureImage source, CancellationToken token = default)
        {
            return LogCompositionAsync(ComposeScrollCoreAsync(region, source, token), region, 8, false);
        }

        private Task<byte[]> ComposeScrollCoreAsync(PixelRect region, ScrollCaptureImage source,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (source.Width <= 0 || source.Height <= 0 ||
                (long)source.Width * source.Height * 4 != source.Pixels.Length)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(source));
            CommitTextEdit();
            return ComposeImageAsync(region, cancellation =>
            {
                cancellation.ThrowIfCancellationRequested();
                var pixels = source.Pixels;
                if (source.Width != region.Width || source.Height != region.Height)
                {
                    if (region.IsEmpty || region.X < 0 || region.Y < 0 || region.Right > source.Width ||
                        region.Bottom > source.Height)
                        throw new ArgumentOutOfRangeException(nameof(region));
                    pixels = new byte[checked(region.Width * region.Height * 4)];
                    for (var y = 0; y < region.Height; y++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        Buffer.BlockCopy(source.Pixels, ((region.Y + y) * source.Width + region.X) * 4, pixels,
                            y * region.Width * 4, region.Width * 4);
                    }
                }

                return new(pixels,
                    new PixelReadSettings((uint)region.Width, (uint)region.Height, StorageType.Char,
                        PixelMapping.BGRA));
            }, token);
        }

        private async Task<byte[]> ComposeImageAsync(PixelRect region, Func<CancellationToken, MagickImage> load,
            CancellationToken token, bool rawOutput = false)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            var annotations = _annotations.ToArray();
            var gesture = _gesture;
            var visible = AnnotationsVisible;
            var moving = rawOutput && _moving is not null ? _selected : null;
            ScreenshotAnnotation? previewMoving = null;
            ScreenshotAnnotation? previewGesture = null;
            if (moving?.Tool is ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.Magnifier
                or ScreenshotTool.Eraser)
                previewMoving = CloneAnnotation(moving);
            if (rawOutput && gesture?.Tool is ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.Magnifier
                    or ScreenshotTool.Eraser)
                previewGesture = CloneAnnotation(gesture);
            try
            {
                if (previewMoving is not null)
                {
                    previewMoving.ImageTiles = await CreateImageEffectAsync(previewMoving, token);
                    var index = Array.IndexOf(annotations, moving);
                    if (index >= 0) annotations[index] = previewMoving;
                }

                if (previewGesture is not null)
                {
                    previewGesture.ImageTiles = await CreateImageEffectAsync(previewGesture, token);
                    gesture = previewGesture;
                }

                if (gesture is not null) annotations = [.. annotations, gesture];
                using var source = await Task.Run(() => load(token), token);
                source.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
                // A grayscale/indexed PNG retains its original encoder type after compositing.
                // Expand the base before applying colored tiles and explicitly encode RGBA.
                source.ColorSpace = ColorSpace.sRGB;
                source.ColorType = ColorType.TrueColorAlpha;
                source.Depth = Math.Max(8u, source.Depth);
                source.Settings.SetDefine(MagickFormat.Png, "color-type", "6");
                token.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (source.Width != region.Width || source.Height != region.Height)
                    throw new InvalidOperationException(UiText.Get("InvalidFrameBuffer"));
                if (visible)
                {
                    var layer = new List<ScreenshotAnnotation>();
                    foreach (var annotation in annotations)
                        if (annotation.Tool is ScreenshotTool.Blur or ScreenshotTool.Mosaic or ScreenshotTool.Eraser
                            or ScreenshotTool.Magnifier)
                        {
                            await CompositeAnnotationLayerAsync(source, region, layer, token);
                            layer.Clear();
                            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                            switch (annotation.Tool)
                            {
                                case ScreenshotTool.Eraser:
                                    await EraseImageLayerAsync(source, region, annotation, token);
                                    break;
                                case ScreenshotTool.Magnifier:
                                    await CopyMagnifierLayerAsync(source, region, annotation, token);
                                    await CompositeAnnotationLayerAsync(source, region, [annotation], token, false);
                                    break;
                                case ScreenshotTool.Blur:
                                case ScreenshotTool.Mosaic:
                                    await CopyImageEffectLayerAsync(source, region, annotation, token);
                                    break;
                            }
                        }
                        else
                        {
                            layer.Add(annotation);
                        }

                    await CompositeAnnotationLayerAsync(source, region, layer, token);
                }

                return await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    byte[] result;
                    if (rawOutput)
                    {
                        using var pixels = source.GetPixels();
                        result = pixels.ToByteArray(PixelMapping.BGRA)!;
                    }
                    else
                    {
                        result = source.ToByteArray(MagickFormat.Png);
                    }

                    token.ThrowIfCancellationRequested();
                    return result;
                }, token);
            }
            catch (MagickException) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }
            finally
            {
                previewMoving?.Dispose();
                previewGesture?.Dispose();
            }
        }

        private Rect RenderBounds(ScreenshotAnnotation annotation)
        {
            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (annotation.Tool)
            {
                case ScreenshotTool.Spotlight:
                case ScreenshotTool.Watermark:
                    return new(AnnotationBounds.X, AnnotationBounds.Y, AnnotationBounds.Width, AnnotationBounds.Height);
                case ScreenshotTool.Text:
                case ScreenshotTool.Number:
                    var text = FormatLabel(annotation, Brushes.Black);
                    var padding = annotation.Properties.TextPadding * 2;
                    var size = annotation.Tool == ScreenshotTool.Number
                        ? new Size(Math.Max(annotation.FontSize * 1.7, text.Width + padding),
                            Math.Max(annotation.FontSize * 1.7, text.Width + padding))
                        : new Size(text.Width + padding, text.Height + padding);
                    return new Rect(annotation.Points[0], size).Inflate(annotation.Properties.TextOutlineWidth);
            }

            var left = annotation.Points.Min(point => point.X);
            var top = annotation.Points.Min(point => point.Y);
            var right = annotation.Points.Max(point => point.X);
            var bottom = annotation.Points.Max(point => point.Y);
            var bounds = new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top)).Inflate(Math.Max(12,
                annotation.Thickness * 4));
            if (annotation.Tool != ScreenshotTool.Arrow || string.IsNullOrWhiteSpace(annotation.Text)) return bounds;
            var comment = FormatLabel(annotation, Brushes.Black);
            return bounds.Union(new(annotation.Points[^1] + new Vector(8, 8), new Size(comment.Width, comment.Height)));
        }
    }
}

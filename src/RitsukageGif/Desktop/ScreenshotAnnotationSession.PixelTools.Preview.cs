using Avalonia;
using Avalonia.Media.Imaging;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private async Task<byte[]> ComposePixelPreviewRegionAsync(PixelRect region, PixelRect sampling,
            byte[] visible, CancellationToken token)
        {
            var frames = PixelPreviewFrames();
            var pixels = await Task.Run(() =>
            {
                var buffer = ReadPixelPreviewSource(sampling, frames, token);
                for (var y = 0; y < region.Height; y++)
                    Buffer.BlockCopy(visible, y * region.Width * 4, buffer,
                        ((region.Y + y - sampling.Y) * sampling.Width + region.X - sampling.X) * 4, region.Width * 4);
                return buffer;
            }, token);
            var composed = await ComposeImageAsync(sampling, cancellation =>
            {
                cancellation.ThrowIfCancellationRequested();
                return new(pixels, new PixelReadSettings((uint)sampling.Width, (uint)sampling.Height,
                    StorageType.Char, PixelMapping.BGRA));
            }, token, true);
            return await Task.Run(() =>
            {
                var result = new byte[visible.Length];
                for (var y = 0; y < region.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    Buffer.BlockCopy(composed,
                        ((region.Y + y - sampling.Y) * sampling.Width + region.X - sampling.X) * 4,
                        result, y * region.Width * 4, region.Width * 4);
                }

                return result;
            }, token);
        }

        private async Task<IReadOnlyList<(Bitmap Image, Rect Bounds)>> CreatePixelPreviewAsync(
            ScreenshotAnnotation annotation, ScreenshotAnnotation[] preceding, CancellationToken token,
            (PixelRect Region, byte[] Pixels)[]? sourceFrames = null)
        {
            var region = PixelArea(annotation, AnnotationBounds);
            if (region.IsEmpty) return [];
            var sampling = PixelSamplingArea(annotation, region, AnnotationBounds);
            sampling = ExpandPixelPreviewRegion(sampling, preceding);
            var frames = sourceFrames ?? PixelPreviewFrames();
            var pixels = await Task.Run(() => ReadPixelPreviewSource(sampling, frames, token), token);
            var composed = await ComposeImageAsync(sampling, cancellation =>
            {
                cancellation.ThrowIfCancellationRequested();
                return new(pixels, new PixelReadSettings((uint)sampling.Width, (uint)sampling.Height,
                    StorageType.Char, PixelMapping.BGRA));
            }, token, true, preceding, frames);
            if (annotation.Tool == ScreenshotTool.SmartErase)
            {
                using var source = new MagickImage(composed,
                    new PixelReadSettings((uint)sampling.Width, (uint)sampling.Height, StorageType.Char,
                        PixelMapping.BGRA));
                await ApplyInpaintingLayerAsync(source, sampling, annotation,
                    annotation.Properties.InpaintingRadius, token, false);
                using var restored = source.GetPixels();
                composed = restored.ToByteArray(PixelMapping.BGRA)!;
            }
            else
            {
                await Task.Run(() => PixelEffectProcessor.Apply(composed, sampling.Width, sampling.Height,
                [
                    PixelFilter(annotation, new(region.X - sampling.X, region.Y - sampling.Y,
                        region.Width, region.Height)),
                ], token: token), token);
            }

            return await CreateMosaicAsync(new(region.X, region.Y, region.Width, region.Height), 1, token,
                [(sampling, composed)]);
        }

        private PixelRect ExpandPixelPreviewRegion(PixelRect sampling, IEnumerable<ScreenshotAnnotation> preceding)
        {
            foreach (var layer in preceding.Reverse())
            {
                if (layer.Tool == ScreenshotTool.Magnifier)
                {
                    if (!MagnifierPixelBounds(MagnifierLensBounds(layer)).Intersect(sampling).IsEmpty)
                    {
                        var source = MagnifierPixelBounds(layer.Bounds).Intersect(AnnotationBounds);
                        var sourceLeft = Math.Min(sampling.X, source.X);
                        var sourceTop = Math.Min(sampling.Y, source.Y);
                        sampling = new(sourceLeft, sourceTop, Math.Max(sampling.Right, source.Right) - sourceLeft,
                            Math.Max(sampling.Bottom, source.Bottom) - sourceTop);
                    }

                    continue;
                }

                if (layer.Tool is not (ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.SmartErase))
                    continue;
                var area = PixelArea(layer, AnnotationBounds);
                if (area.Intersect(sampling).IsEmpty) continue;
                var full = PixelSamplingArea(layer, area, AnnotationBounds);
                var left = Math.Min(sampling.X, full.X);
                var top = Math.Min(sampling.Y, full.Y);
                sampling = new(left, top, Math.Max(sampling.Right, full.Right) - left,
                    Math.Max(sampling.Bottom, full.Bottom) - top);
            }

            return sampling;
        }

        private (PixelRect Region, byte[] Pixels)[] PixelPreviewFrames()
        {
            return _overlayFrame is { } overlay ? [.. _frames, overlay] : [.. _frames];
        }

        private static byte[] ReadPixelPreviewSource(PixelRect region,
            (PixelRect Region, byte[] Pixels)[] frames, CancellationToken token)
        {
            if ((long)region.Width * region.Height > 20L * 1024 * 1024)
                throw new InvalidOperationException(UiText.Get("RecordingTooLarge"));
            var result = new byte[checked(region.Width * region.Height * 4)];
            foreach (var frame in frames)
            {
                var area = frame.Region.Intersect(region);
                for (var y = area.Y; y < area.Bottom; y++)
                {
                    token.ThrowIfCancellationRequested();
                    Buffer.BlockCopy(frame.Pixels,
                        ((y - frame.Region.Y) * frame.Region.Width + area.X - frame.Region.X) * 4,
                        result, ((y - region.Y) * region.Width + area.X - region.X) * 4, area.Width * 4);
                }
            }

            return result;
        }
    }
}

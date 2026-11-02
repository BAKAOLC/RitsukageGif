using System.Buffers.Binary;
using Avalonia;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private static PixelRect MagnifierPixelBounds(Rect bounds)
        {
            var x = (int)Math.Floor(bounds.X);
            var y = (int)Math.Floor(bounds.Y);
            return new(x, y, Math.Max(1, (int)Math.Ceiling(bounds.Right) - x),
                Math.Max(1, (int)Math.Ceiling(bounds.Bottom) - y));
        }

        private static void SampleMagnifierPixel(MagnifierPixelSource source, ScreenshotAnnotation annotation,
            Rect lens, int x, int y, Span<double> result)
        {
            var bounds = annotation.Bounds;
            var scale = annotation.Properties.Magnification;
            var sx = bounds.X + (x + .5 - lens.X) / scale - .5;
            var sy = bounds.Y + (y + .5 - lens.Y) / scale - .5;
            var available = source.Bounds;
            if (!annotation.Properties.MagnifierAntialias)
            {
                var px = Math.Clamp((int)Math.Floor(sx + .5), available.X, available.Right - 1);
                var py = Math.Clamp((int)Math.Floor(sy + .5), available.Y, available.Bottom - 1);
                for (var channel = 0; channel < 4; channel++) result[channel] = source.Read(px, py, channel);
                return;
            }

            var x0 = (int)Math.Floor(sx);
            var y0 = (int)Math.Floor(sy);
            var fx = sx - x0;
            var fy = sy - y0;
            result.Clear();
            for (var row = 0; row < 2; row++)
            for (var column = 0; column < 2; column++)
            {
                var px = Math.Clamp(x0 + column, available.X, available.Right - 1);
                var py = Math.Clamp(y0 + row, available.Y, available.Bottom - 1);
                var weight = (column == 0 ? 1 - fx : fx) * (row == 0 ? 1 - fy : fy);
                var alpha = source.Read(px, py, 3) * weight;
                for (var channel = 0; channel < 3; channel++) result[channel] += source.Read(px, py, channel) * alpha;
                result[3] += alpha;
            }

            if (result[3] > 0)
                for (var channel = 0; channel < 3; channel++)
                    result[channel] /= result[3];
        }

        private static void BlendMagnifierPixel(Span<double> sample, ReadOnlySpan<double> behind, double coverage)
        {
            var foreground = sample[3] * coverage;
            var background = behind[3] * (1 - coverage);
            var alpha = foreground + background;
            for (var channel = 0; channel < 3; channel++)
                sample[channel] = alpha > 0 ? (sample[channel] * foreground + behind[channel] * background) / alpha : 0;
            sample[3] = alpha;
        }

        private async Task CopyMagnifierLayerAsync(MagickImage source, PixelRect region,
            ScreenshotAnnotation annotation, CancellationToken token)
        {
            var sampleRegion = MagnifierPixelBounds(annotation.Bounds).Intersect(region);
            if (sampleRegion.IsEmpty) return;
            byte[] pixels;
            if (annotation.Properties.MagnifierShowAnnotations)
            {
                pixels = await Task.Run(() =>
                {
                    using var collection = source.GetPixels();
                    var current = collection.GetArea(sampleRegion.X - region.X, sampleRegion.Y - region.Y,
                        (uint)sampleRegion.Width, (uint)sampleRegion.Height)!;
                    for (var index = 0; index < current.Length; index += 4)
                        (current[index], current[index + 2]) = (current[index + 2], current[index]);
                    return current;
                }, token);
            }
            else
            {
                var frames = PixelPreviewFrames();
                pixels = await Task.Run(() => ReadPixelPreviewSource(sampleRegion, frames, token), token);
            }

            var sampling = new MagnifierPixelSource(pixels, sampleRegion, sampleRegion.Width * 4);
            var lens = MagnifierLensBounds(annotation);
            var destination = MagnifierPixelBounds(lens).Intersect(region).Intersect(AnnotationBounds);
            const int tileSize = 512;
            for (var top = destination.Y; top < destination.Bottom; top += tileSize)
            for (var left = destination.X; left < destination.Right; left += tileSize)
            {
                var tile = new PixelRect(left, top, Math.Min(tileSize, destination.Right - left),
                    Math.Min(tileSize, destination.Bottom - top));
                await Task.Run(() =>
                {
                    using var collection = source.GetPixels();
                    var target = collection.GetArea(tile.X - region.X, tile.Y - region.Y,
                        (uint)tile.Width, (uint)tile.Height)!;
                    Span<double> sample = stackalloc double[4];
                    Span<double> behind = stackalloc double[4];
                    for (var y = 0; y < tile.Height; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        for (var x = 0; x < tile.Width; x++)
                        {
                            var coverage = MagnifierCoverage(annotation, lens, tile.X + x, tile.Y + y);
                            if (coverage == 0) continue;
                            var index = (y * tile.Width + x) * 4;
                            SampleMagnifierPixel(sampling, annotation, lens, tile.X + x, tile.Y + y, sample);
                            for (var channel = 0; channel < 4; channel++)
                                behind[channel] = target[index + channel] / 255d;
                            BlendMagnifierPixel(sample, behind, coverage);
                            for (var channel = 0; channel < 4; channel++)
                                target[index + channel] = (byte)Math.Clamp(Math.Round(sample[channel] * 255), 0, 255);
                        }
                    }

                    collection.SetArea(tile.X - region.X, tile.Y - region.Y, (uint)tile.Width, (uint)tile.Height,
                        target);
                }, token);
            }
        }

        private void MagnifyHighPrecisionLayer(byte[] output, PngRaster source, PixelRect region,
            ScreenshotAnnotation annotation, CancellationToken token)
        {
            var sampleRegion = MagnifierPixelBounds(annotation.Bounds).Intersect(region);
            if (sampleRegion.IsEmpty) return;
            var pixels = CropMagnifierSamples(annotation.Properties.MagnifierShowAnnotations ? output : source.Samples,
                region, source.Width * 8, sampleRegion, token);
            var sampling = new MagnifierPixelSource(pixels, sampleRegion, sampleRegion.Width * 8, 1);
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
                    var index = ((y - region.Y) * source.Width + x - region.X) * 8;
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

        private void MagnifyHdrLayer(byte[] output, CapturedFrame destination, PixelRect region,
            CapturedFrame original, PixelRect originalRegion, ScreenshotAnnotation annotation, CancellationToken token)
        {
            var showAnnotations = annotation.Properties.MagnifierShowAnnotations;
            var sampleRegion = MagnifierPixelBounds(annotation.Bounds)
                .Intersect(showAnnotations ? region : originalRegion);
            if (sampleRegion.IsEmpty) return;
            var pixels = CropMagnifierSamples(showAnnotations ? output : original.Pixels,
                showAnnotations ? region : originalRegion, showAnnotations ? destination.Stride : original.Stride,
                sampleRegion, token);
            var sampling = showAnnotations
                ? new MagnifierPixelSource(pixels, sampleRegion, sampleRegion.Width * 8, 2, destination.AlphaMode)
                : new MagnifierPixelSource(pixels, sampleRegion, sampleRegion.Width * 8, 2, original.AlphaMode);
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
                    var index = (y - region.Y) * destination.Stride + (x - region.X) * 8;
                    var alpha = destination.AlphaMode == CaptureAlphaMode.Opaque
                        ? 1
                        : (double)BitConverter.UInt16BitsToHalf(
                            BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(index + 6)));
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var value = (double)BitConverter.UInt16BitsToHalf(
                            BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(index + channel * 2)));
                        behind[channel] = destination.AlphaMode == CaptureAlphaMode.Premultiplied
                            ? alpha > 0 ? value / alpha : 0
                            : value;
                    }

                    behind[3] = alpha;
                    BlendMagnifierPixel(sample, behind, coverage);
                    for (var channel = 0; channel < 4; channel++)
                    {
                        var value = channel < 3 && destination.AlphaMode == CaptureAlphaMode.Premultiplied
                            ? sample[channel] * sample[3]
                            : sample[channel];
                        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(index + channel * 2),
                            BitConverter.HalfToUInt16Bits((Half)value));
                    }
                }
            }
        }

        private static byte[] CropMagnifierSamples(ReadOnlyMemory<byte> pixels, PixelRect bounds, int stride,
            PixelRect sampleRegion, CancellationToken token)
        {
            var width = checked(sampleRegion.Width * 8);
            var result = new byte[checked(width * sampleRegion.Height)];
            for (var y = 0; y < sampleRegion.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                pixels.Span.Slice((sampleRegion.Y + y - bounds.Y) * stride + (sampleRegion.X - bounds.X) * 8, width)
                    .CopyTo(result.AsSpan(y * width));
            }

            return result;
        }
    }
}

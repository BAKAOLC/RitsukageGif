using System.Numerics;
using System.Runtime.InteropServices;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private Task ApplyInpaintingLayerAsync(MagickImage source, PixelRect region,
            ScreenshotAnnotation annotation, int radius, CancellationToken token, bool blendCoverage = true)
        {
            return Task.Run(() =>
            {
                var area = PixelArea(annotation, region);
                if (area.IsEmpty) return;
                var sampling = ExpandSampleRegion(area, region.Intersect(AnnotationBounds), radius + 2);
                using var collection = source.GetPixels();
                var pixels = collection.GetArea(sampling.X - region.X, sampling.Y - region.Y,
                    (uint)sampling.Width, (uint)sampling.Height)!;
                var coverage = CreatePixelCoverage(annotation, sampling, token);
                if (!blendCoverage)
                    for (var index = 0; index < coverage.Length; index++)
                        if (coverage[index] != 0)
                            coverage[index] = ushort.MaxValue;
                ImageInpainting.RestoreRgba(pixels, sampling.Width, sampling.Height, coverage, radius, token);
                token.ThrowIfCancellationRequested();
                collection.SetArea(sampling.X - region.X, sampling.Y - region.Y,
                    (uint)sampling.Width, (uint)sampling.Height, pixels);
            }, token);
        }

        private void ApplyInpaintingLayer16(byte[] output, PngRaster source, PixelRect region,
            ScreenshotAnnotation annotation, int radius, CancellationToken token)
        {
            var area = PixelArea(annotation, region);
            if (area.IsEmpty) return;
            var sampling = ExpandSampleRegion(area, region.Intersect(AnnotationBounds), radius + 2);
            var original = SnapshotPixelArea(output, source.Width * 8, region, sampling, token);
            var coverage = CreatePixelCoverage(annotation, sampling, token);
            var restored = ImageInpainting.RestorePng(new(sampling.Width, sampling.Height, 16, 6, original,
                source.Metadata), coverage, radius, token);
            for (var y = 0; y < area.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < area.Width; x++)
                {
                    var index = (area.Y + y - sampling.Y) * sampling.Width + area.X + x - sampling.X;
                    if (coverage[index] == 0) continue;
                    restored.Samples.AsSpan(index * 8, 8).CopyTo(output.AsSpan(
                        ((area.Y + y - region.Y) * source.Width + area.X + x - region.X) * 8, 8));
                }
            }
        }

        private void ApplyInpaintingLayerHdr(byte[] output, CapturedFrame source, PixelRect region,
            ScreenshotAnnotation annotation, int radius, CancellationToken token)
        {
            var area = PixelArea(annotation, region);
            if (area.IsEmpty) return;
            var sampling = ExpandSampleRegion(area, region.Intersect(AnnotationBounds), radius + 2);
            var original = SnapshotPixelArea(output, source.Stride, region, sampling, token);
            var samples = MemoryMarshal.Cast<byte, Half>(original);
            var pixels = new Vector4[checked(sampling.Width * sampling.Height)];
            for (var index = 0; index < pixels.Length; index++)
            {
                if ((index & 4095) == 0) token.ThrowIfCancellationRequested();
                var alpha = source.AlphaMode == CaptureAlphaMode.Opaque
                    ? 1
                    : Math.Clamp((float)samples[index * 4 + 3], 0, 1);
                var scale = source.AlphaMode == CaptureAlphaMode.Premultiplied ? 1 : alpha;
                pixels[index] = new((float)samples[index * 4] * scale, (float)samples[index * 4 + 1] * scale,
                    (float)samples[index * 4 + 2] * scale, alpha);
            }

            var coverage = CreatePixelCoverage(annotation, sampling, token);
            ImageInpainting.Restore(pixels, sampling.Width, sampling.Height, coverage, radius, token);
            Span<Half> restored = stackalloc Half[4];
            for (var y = 0; y < area.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < area.Width; x++)
                {
                    var index = (area.Y + y - sampling.Y) * sampling.Width + area.X + x - sampling.X;
                    var amount = BitOperations.PopCount(coverage[index]);
                    if (amount == 0) continue;
                    var value = pixels[index];
                    var scale = source.AlphaMode == CaptureAlphaMode.Premultiplied ? 1 : value.W > 0 ? 1 / value.W : 0;
                    restored[0] = (Half)Math.Clamp(value.X * scale, -65504, 65504);
                    restored[1] = (Half)Math.Clamp(value.Y * scale, -65504, 65504);
                    restored[2] = (Half)Math.Clamp(value.Z * scale, -65504, 65504);
                    restored[3] = (Half)(source.AlphaMode == CaptureAlphaMode.Opaque ? 1 : value.W);
                    var target = output.AsSpan((area.Y + y - region.Y) * source.Stride + (area.X + x - region.X) * 8,
                        8);
                    MixPixelHdr(MemoryMarshal.AsBytes(restored), target, amount, source.AlphaMode);
                }
            }
        }
    }
}

using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private PixelRect PixelArea(ScreenshotAnnotation annotation, PixelRect region)
        {
            if (annotation.AutoMosaicId != Guid.Empty && annotation.AutoMosaicRegions.IsDefaultOrEmpty) return default;
            var bounds = PixelEffectBounds(annotation);
            return new PixelRect((int)Math.Floor(bounds.Left), (int)Math.Floor(bounds.Top),
                    Math.Max(1, (int)Math.Ceiling(bounds.Right) - (int)Math.Floor(bounds.Left)),
                    Math.Max(1, (int)Math.Ceiling(bounds.Bottom) - (int)Math.Floor(bounds.Top)))
                .Intersect(region).Intersect(AnnotationBounds);
        }

        private PixelRect PixelSamplingArea(ScreenshotAnnotation annotation, PixelRect area, PixelRect region)
        {
            if (annotation.Tool == ScreenshotTool.SmartErase)
                return ExpandSampleRegion(area, region.Intersect(AnnotationBounds),
                    annotation.Properties.InpaintingRadius + 2);
            return annotation.Tool == ScreenshotTool.Blur
                ? ExpandSampleRegion(area, region.Intersect(AnnotationBounds), annotation.Properties.BlurRadius)
                : area;
        }

        private static PixelEffect PixelFilter(ScreenshotAnnotation annotation, PixelRect local)
        {
            return annotation.Tool == ScreenshotTool.Blur
                ? new(PixelEffectKind.Blur, local, Radius: annotation.Properties.BlurRadius)
                : new(PixelEffectKind.Mosaic, local, BlockSize: annotation.MosaicSize);
        }

        private Task ApplyPixelLayerAsync(MagickImage source, PixelRect region,
            ScreenshotAnnotation annotation, CancellationToken token)
        {
            return Task.Run(() =>
            {
                var area = PixelArea(annotation, region);
                if (area.IsEmpty) return;
                var sampling = PixelSamplingArea(annotation, area, region);
                using var collection = source.GetPixels();
                var sample = collection.GetArea(sampling.X - region.X, sampling.Y - region.Y,
                    (uint)sampling.Width, (uint)sampling.Height)!;
                var local = new PixelRect(area.X - sampling.X, area.Y - sampling.Y, area.Width, area.Height);
                PixelEffectProcessor.Apply(sample, sampling.Width, sampling.Height, [PixelFilter(annotation, local)],
                    token: token);
                var destination = collection.GetArea(area.X - region.X, area.Y - region.Y,
                    (uint)area.Width, (uint)area.Height)!;
                var coverage = CreatePixelCoverage(annotation, area, token);
                for (var y = 0; y < area.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var x = 0; x < area.Width; x++)
                    {
                        var amount = BitOperations.PopCount(coverage[y * area.Width + x]);
                        if (amount == 0) continue;
                        var input = sample.AsSpan(((local.Y + y) * sampling.Width + local.X + x) * 4, 4);
                        var target = destination.AsSpan((y * area.Width + x) * 4, 4);
                        MixPixel8(input, target, amount);
                    }
                }

                collection.SetArea(area.X - region.X, area.Y - region.Y, (uint)area.Width, (uint)area.Height,
                    destination);
            }, token);
        }

        private void ApplyPixelLayer16(byte[] output, PngRaster source, PixelRect region,
            ScreenshotAnnotation annotation, CancellationToken token)
        {
            var area = PixelArea(annotation, region);
            if (area.IsEmpty) return;
            var sampling = PixelSamplingArea(annotation, area, region);
            var original = SnapshotPixelArea(output, source.Width * 8, region, sampling, token);
            var processed = original.ToArray();
            var local = new PixelRect(area.X - sampling.X, area.Y - sampling.Y, area.Width, area.Height);
            if (annotation.Tool == ScreenshotTool.Blur)
                Png16PixelProcessor.Blur(processed, original, sampling.Width, sampling.Height, local,
                    annotation.Properties.BlurRadius, token);
            else
                Png16PixelProcessor.Mosaic(processed, original, sampling.Width, sampling.Height, local,
                    annotation.MosaicSize, token);
            var coverage = CreatePixelCoverage(annotation, area, token);
            for (var y = 0; y < area.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < area.Width; x++)
                {
                    var amount = BitOperations.PopCount(coverage[y * area.Width + x]);
                    if (amount == 0) continue;
                    var input = processed.AsSpan(((local.Y + y) * sampling.Width + local.X + x) * 8, 8);
                    var target = output.AsSpan(((area.Y + y - region.Y) * source.Width + area.X + x - region.X) * 8, 8);
                    MixPixel16(input, target, amount);
                }
            }
        }

        private void ApplyPixelLayerHdr(byte[] output, CapturedFrame source, PixelRect region,
            ScreenshotAnnotation annotation, CancellationToken token)
        {
            var area = PixelArea(annotation, region);
            if (area.IsEmpty) return;
            var sampling = PixelSamplingArea(annotation, area, region);
            var original = SnapshotPixelArea(output, source.Stride, region, sampling, token);
            var frame = new CapturedFrame(sampling.Width, sampling.Height, sampling.Width * 8,
                source.PixelFormat, original, source.ColorInfo, source.AlphaMode, source.Revision, []);
            var local = new PixelRect(area.X - sampling.X, area.Y - sampling.Y, area.Width, area.Height);
            var processed = HdrPixelEffectProcessor.Apply(frame, [PixelFilter(annotation, local)], token);
            var coverage = CreatePixelCoverage(annotation, area, token);
            for (var y = 0; y < area.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < area.Width; x++)
                {
                    var amount = BitOperations.PopCount(coverage[y * area.Width + x]);
                    if (amount == 0) continue;
                    var input = processed.Pixels.Span.Slice(((local.Y + y) * sampling.Width + local.X + x) * 8, 8);
                    var target = output.AsSpan((area.Y + y - region.Y) * source.Stride + (area.X + x - region.X) * 8,
                        8);
                    MixPixelHdr(input, target, amount, source.AlphaMode);
                }
            }
        }

        private static byte[] SnapshotPixelArea(byte[] pixels, int stride, PixelRect region, PixelRect sampling,
            CancellationToken token)
        {
            var rowBytes = checked(sampling.Width * 8);
            var snapshot = new byte[checked(rowBytes * sampling.Height)];
            for (var row = 0; row < sampling.Height; row++)
            {
                token.ThrowIfCancellationRequested();
                pixels.AsSpan((sampling.Y + row - region.Y) * stride + (sampling.X - region.X) * 8, rowBytes)
                    .CopyTo(snapshot.AsSpan(row * rowBytes, rowBytes));
            }

            return snapshot;
        }

        private static void MixPixel8(ReadOnlySpan<byte> input, Span<byte> target, int amount)
        {
            if (amount == 16)
            {
                input.CopyTo(target);
                return;
            }

            var front = input[3] * amount;
            var back = target[3] * (16 - amount);
            var weight = front + back;
            for (var channel = 0; channel < 3; channel++)
                target[channel] = weight > 0
                    ? (byte)((input[channel] * front + target[channel] * back + weight / 2) / weight)
                    : (byte)0;
            target[3] = (byte)((weight + 8) / 16);
        }

        private static void MixPixel16(ReadOnlySpan<byte> input, Span<byte> target, int amount)
        {
            if (amount == 16)
            {
                input.CopyTo(target);
                return;
            }

            var front = (ulong)BinaryPrimitives.ReadUInt16BigEndian(input[6..]) * (uint)amount;
            var back = (ulong)BinaryPrimitives.ReadUInt16BigEndian(target[6..]) * (uint)(16 - amount);
            var weight = front + back;
            for (var channel = 0; channel < 3; channel++)
                BinaryPrimitives.WriteUInt16BigEndian(target[(channel * 2)..], weight > 0
                    ? (ushort)((BinaryPrimitives.ReadUInt16BigEndian(input[(channel * 2)..]) * front +
                                BinaryPrimitives.ReadUInt16BigEndian(target[(channel * 2)..]) * back + weight / 2) /
                               weight)
                    : (ushort)0);
            BinaryPrimitives.WriteUInt16BigEndian(target[6..], (ushort)((weight + 8) / 16));
        }

        private static void MixPixelHdr(ReadOnlySpan<byte> input, Span<byte> target, int amount, CaptureAlphaMode mode)
        {
            if (amount == 16)
            {
                input.CopyTo(target);
                return;
            }

            var front = MemoryMarshal.Cast<byte, Half>(input);
            var back = MemoryMarshal.Cast<byte, Half>(target);
            var alphaFront = mode == CaptureAlphaMode.Opaque ? 1 : Math.Clamp((double)front[3], 0, 1);
            var alphaBack = mode == CaptureAlphaMode.Opaque ? 1 : Math.Clamp((double)back[3], 0, 1);
            var coverage = amount / 16d;
            var alpha = alphaFront * coverage + alphaBack * (1 - coverage);
            for (var channel = 0; channel < 3; channel++)
            {
                var first = (double)front[channel];
                var second = (double)back[channel];
                if (mode != CaptureAlphaMode.Premultiplied)
                {
                    first *= alphaFront;
                    second *= alphaBack;
                }

                var value = first * coverage + second * (1 - coverage);
                if (mode != CaptureAlphaMode.Premultiplied) value = alpha > 0 ? value / alpha : 0;
                back[channel] = (Half)Math.Clamp(value, -65504, 65504);
            }

            back[3] = (Half)(mode == CaptureAlphaMode.Opaque ? 1 : alpha);
        }
    }
}

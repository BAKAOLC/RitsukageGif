using System.Buffers.Binary;

namespace RitsukageGif.Desktop
{
    internal static class HdrAnnotationBlender
    {
        private static readonly double[] LinearSamples =
            [.. Enumerable.Range(0, 256).Select(value => ToLinear(value / 255d))];

        public static Task<CapturedFrame> CompositeAsync(CapturedFrame source, PixelRect globalFrameRegion,
            byte[] bgra, PixelRect globalOverlayRegion, CancellationToken token = default)
        {
            if (source.Width != globalFrameRegion.Width || source.Height != globalFrameRegion.Height)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(globalFrameRegion));
            var overlay = new CapturedFrame(globalOverlayRegion.Width, globalOverlayRegion.Height,
                checked(globalOverlayRegion.Width * 4), CapturePixelFormat.Bgra8Srgb, bgra,
                new(false), CaptureAlphaMode.Straight, source.Revision, []);
            return CompositeAsync(source, overlay, new(checked(globalOverlayRegion.X - globalFrameRegion.X),
                checked(globalOverlayRegion.Y - globalFrameRegion.Y), globalOverlayRegion.Width,
                globalOverlayRegion.Height), token);
        }

        private static Task<CapturedFrame> CompositeAsync(CapturedFrame source, CapturedFrame overlay,
            PixelRect localRegion, CancellationToken token)
        {
            Validate(source);
            if (overlay.PixelFormat != CapturePixelFormat.Bgra8Srgb || overlay.Width != localRegion.Width ||
                overlay.Height != localRegion.Height || overlay.Width <= 0 || overlay.Height <= 0 ||
                overlay.Stride < (long)overlay.Width * 4 ||
                overlay.Pixels.Length < (long)overlay.Stride * overlay.Height ||
                !Enum.IsDefined(overlay.AlphaMode))
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(overlay));
            return Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var output = source.Pixels[..checked(source.Stride * source.Height)].ToArray();
                Blend(output, source, localRegion, overlay.Pixels.Span, overlay.Stride, overlay.AlphaMode, token);
                return source with { Pixels = output };
            }, token);
        }

        internal static void Validate(CapturedFrame source)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (source.PixelFormat != CapturePixelFormat.Rgba16FloatScRgb || source.Width <= 0 || source.Height <= 0 ||
                source.Stride < (long)source.Width * 8 || (long)source.Stride * source.Height > 512L * 1024 * 1024 ||
                source.Pixels.Length < (long)source.Stride * source.Height || !Enum.IsDefined(source.AlphaMode) ||
                !ValidWhite(source.ColorInfo.SdrWhiteNits) ||
                source.ColorRegions.Any(region => !ValidWhite(region.ColorInfo.SdrWhiteNits)))
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(source));
        }

        private static bool ValidWhite(float white)
        {
            return float.IsFinite(white) && white > 0;
        }

        internal static void Blend(byte[] output, CapturedFrame source, PixelRect localRegion,
            ReadOnlySpan<byte> bgra, int stride, CaptureAlphaMode overlayAlpha, CancellationToken token)
        {
            var clipped = localRegion.Intersect(new(0, 0, source.Width, source.Height));
            for (var y = clipped.Y; y < clipped.Bottom; y++)
            {
                token.ThrowIfCancellationRequested();
                var inputRow = bgra.Slice((y - localRegion.Y) * stride, stride);
                var destination = output.AsSpan(y * source.Stride, source.Stride);
                for (var x = clipped.X; x < clipped.Right; x++)
                {
                    var input = inputRow.Slice((x - localRegion.X) * 4, 4);
                    var alpha = overlayAlpha == CaptureAlphaMode.Opaque ? 1 : input[3] / 255d;
                    if (alpha <= 0) continue;
                    var target = destination.Slice(x * 8, 8);
                    var behindAlpha = source.AlphaMode == CaptureAlphaMode.Opaque
                        ? 1
                        : Math.Clamp(Read(target[6..]), 0, 1);
                    var resultAlpha = alpha + behindAlpha * (1 - alpha);
                    var white = ReferenceWhite(source, x, y) / FrameColorInfo.ScRgbReferenceWhiteNits;
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var encoded = input[2 - channel];
                        var linear = overlayAlpha == CaptureAlphaMode.Premultiplied
                            ? ToLinear(Math.Clamp(encoded / (255 * alpha), 0, 1))
                            : LinearSamples[encoded];
                        var behind = Read(target[(channel * 2)..]);
                        if (behindAlpha <= 0) behind = 0;
                        else if (source.AlphaMode != CaptureAlphaMode.Premultiplied) behind *= behindAlpha;
                        var value = linear * white * alpha + behind * (1 - alpha);
                        if (source.AlphaMode == CaptureAlphaMode.Straight)
                            value = resultAlpha > 0 ? value / resultAlpha : 0;
                        Write(target[(channel * 2)..], value);
                    }

                    Write(target[6..], source.AlphaMode == CaptureAlphaMode.Opaque ? 1 : resultAlpha);
                }
            }
        }

        internal static void Mosaic(byte[] output, CapturedFrame destination, PixelRect globalFrameRegion,
            CapturedFrame samples, PixelRect globalSampleRegion, PixelRect globalMosaicRegion, int blockSize,
            CancellationToken token)
        {
            var clipped = globalMosaicRegion.Intersect(globalFrameRegion);
            if (clipped.IsEmpty) return;
            var firstX = globalMosaicRegion.X + (clipped.X - globalMosaicRegion.X) / blockSize * blockSize;
            var firstY = globalMosaicRegion.Y + (clipped.Y - globalMosaicRegion.Y) / blockSize * blockSize;
            var original = samples.Pixels.Span;
            for (var by = firstY; by < clipped.Bottom; by += blockSize)
            for (var bx = firstX; bx < clipped.Right; bx += blockSize)
            {
                token.ThrowIfCancellationRequested();
                var block = new PixelRect(bx, by, Math.Min(blockSize, globalMosaicRegion.Right - bx),
                    Math.Min(blockSize, globalMosaicRegion.Bottom - by)).Intersect(globalSampleRegion);
                double red = 0, green = 0, blue = 0, alphaSum = 0;
                for (var y = block.Y; y < block.Bottom; y++)
                {
                    token.ThrowIfCancellationRequested();
                    var row = original.Slice((y - globalSampleRegion.Y) * samples.Stride, samples.Stride);
                    for (var x = block.X; x < block.Right; x++)
                    {
                        var pixel = row.Slice((x - globalSampleRegion.X) * 8, 8);
                        var alpha = samples.AlphaMode == CaptureAlphaMode.Opaque
                            ? 1
                            : Math.Clamp(Read(pixel[6..]), 0, 1);
                        var factor = samples.AlphaMode == CaptureAlphaMode.Premultiplied ? alpha > 0 ? 1 : 0 : alpha;
                        red += Read(pixel) * factor;
                        green += Read(pixel[2..]) * factor;
                        blue += Read(pixel[4..]) * factor;
                        alphaSum += alpha;
                    }
                }

                var count = (double)block.Width * block.Height;
                var divisor = destination.AlphaMode == CaptureAlphaMode.Premultiplied ? count : alphaSum;
                red = divisor > 0 ? red / divisor : 0;
                green = divisor > 0 ? green / divisor : 0;
                blue = divisor > 0 ? blue / divisor : 0;
                var writeRegion = block.Intersect(globalFrameRegion);
                for (var y = writeRegion.Y; y < writeRegion.Bottom; y++)
                {
                    token.ThrowIfCancellationRequested();
                    var row = output.AsSpan((y - globalFrameRegion.Y) * destination.Stride, destination.Stride);
                    for (var x = writeRegion.X; x < writeRegion.Right; x++)
                    {
                        var pixel = row.Slice((x - globalFrameRegion.X) * 8, 8);
                        Write(pixel, red);
                        Write(pixel[2..], green);
                        Write(pixel[4..], blue);
                        Write(pixel[6..], destination.AlphaMode == CaptureAlphaMode.Opaque ? 1 : alphaSum / count);
                    }
                }
            }
        }

        private static double ReferenceWhite(CapturedFrame source, int x, int y)
        {
            foreach (var region in source.ColorRegions)
                if (region.Region.Contains(new(x, y)))
                    return region.ColorInfo.SdrWhiteNits;
            return source.ColorInfo.SdrWhiteNits;
        }

        private static double ToLinear(double encoded)
        {
            return encoded <= .04045 ? encoded / 12.92 : Math.Pow((encoded + .055) / 1.055, 2.4);
        }

        private static double Read(ReadOnlySpan<byte> input)
        {
            var value = (double)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(input));
            return double.IsFinite(value) ? value : 0;
        }

        private static void Write(Span<byte> output, double value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(output,
                BitConverter.HalfToUInt16Bits((Half)Math.Clamp(value, -65504, 65504)));
        }
    }
}

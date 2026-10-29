using System.Numerics;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private static ushort[] CreateEraserCoverage(ScreenshotAnnotation annotation, PixelRect region,
            CancellationToken token)
        {
            var coverage = new ushort[checked(region.Width * region.Height)];
            var radius = annotation.Thickness / 2;
            var squared = radius * radius;
            foreach (var center in EraserSamples(annotation))
            {
                token.ThrowIfCancellationRequested();
                var left = Math.Max(region.X, (int)Math.Floor(center.X - radius));
                var right = Math.Min(region.Right, (int)Math.Ceiling(center.X + radius));
                var top = Math.Max(region.Y, (int)Math.Floor(center.Y - radius));
                var bottom = Math.Min(region.Bottom, (int)Math.Ceiling(center.Y + radius));
                for (var y = top; y < bottom; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var x = left; x < right; x++)
                    {
                        var index = (y - region.Y) * region.Width + x - region.X;
                        if (coverage[index] == ushort.MaxValue) continue;
                        var farX = Math.Max(Math.Abs(x - center.X), Math.Abs(x + 1 - center.X));
                        var farY = Math.Max(Math.Abs(y - center.Y), Math.Abs(y + 1 - center.Y));
                        if (farX * farX + farY * farY <= squared)
                        {
                            coverage[index] = ushort.MaxValue;
                            continue;
                        }

                        for (var subY = 0; subY < 4; subY++)
                        for (var subX = 0; subX < 4; subX++)
                        {
                            var dx = x + (subX + .5) / 4 - center.X;
                            var dy = y + (subY + .5) / 4 - center.Y;
                            if (dx * dx + dy * dy <= squared)
                                coverage[index] |= (ushort)(1 << (subY * 4 + subX));
                        }
                    }
                }
            }

            return coverage;
        }

        private void EraseHdrSamples(byte[] output, CapturedFrame destination, PixelRect globalRegion,
            CapturedFrame samples, PixelRect sampleRegion, ScreenshotAnnotation annotation, CancellationToken token)
        {
            var bounds = RenderBounds(annotation);
            var region = new PixelRect((int)Math.Floor(bounds.X), (int)Math.Floor(bounds.Y),
                    (int)Math.Ceiling(bounds.Right) - (int)Math.Floor(bounds.X),
                    (int)Math.Ceiling(bounds.Bottom) - (int)Math.Floor(bounds.Y))
                .Intersect(globalRegion).Intersect(AnnotationBounds);
            const int tileSize = 512;
            for (var top = region.Y; top < region.Bottom; top += tileSize)
            for (var left = region.X; left < region.Right; left += tileSize)
            {
                var tile = new PixelRect(left, top, Math.Min(tileSize, region.Right - left),
                    Math.Min(tileSize, region.Bottom - top));
                var coverage = CreateEraserCoverage(annotation, tile, token);
                for (var y = 0; y < tile.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var x = 0; x < tile.Width; x++)
                    {
                        var mask = coverage[y * tile.Width + x];
                        if (mask == 0) continue;
                        var original = samples.Pixels.Span.Slice((tile.Y + y - sampleRegion.Y) * samples.Stride +
                                                                 (tile.X + x - sampleRegion.X) * 8, 8);
                        var target = output.AsSpan((tile.Y + y - globalRegion.Y) * destination.Stride +
                                                   (tile.X + x - globalRegion.X) * 8, 8);
                        if (mask == ushort.MaxValue && samples.AlphaMode == destination.AlphaMode)
                        {
                            original.CopyTo(target);
                            continue;
                        }

                        var sourceValues = MemoryMarshal.Cast<byte, Half>(original);
                        var targetValues = MemoryMarshal.Cast<byte, Half>(target);
                        var amount = BitOperations.PopCount(mask) / 16d;
                        var originalAlpha = samples.AlphaMode == CaptureAlphaMode.Opaque
                            ? 1
                            : Math.Clamp((double)sourceValues[3], 0, 1);
                        var targetAlpha = destination.AlphaMode == CaptureAlphaMode.Opaque
                            ? 1
                            : Math.Clamp((double)targetValues[3], 0, 1);
                        var alpha = originalAlpha * amount + targetAlpha * (1 - amount);
                        for (var channel = 0; channel < 3; channel++)
                        {
                            var sourceValue = originalAlpha > 0 ? (double)sourceValues[channel] : 0;
                            var targetValue = targetAlpha > 0 ? (double)targetValues[channel] : 0;
                            if (samples.AlphaMode != CaptureAlphaMode.Premultiplied) sourceValue *= originalAlpha;
                            if (destination.AlphaMode != CaptureAlphaMode.Premultiplied) targetValue *= targetAlpha;
                            var value = sourceValue * amount + targetValue * (1 - amount);
                            if (destination.AlphaMode != CaptureAlphaMode.Premultiplied)
                                value = alpha > 0 ? value / alpha : 0;
                            targetValues[channel] = (Half)Math.Clamp(value, -65504, 65504);
                        }

                        targetValues[3] = (Half)(destination.AlphaMode == CaptureAlphaMode.Opaque ? 1 : alpha);
                    }
                }
            }
        }
    }
}

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private void BlurHdrSamples(byte[] output, CapturedFrame destination, PixelRect globalRegion,
            CapturedFrame samples, PixelRect sampleRegion, ScreenshotAnnotation annotation, CancellationToken token)
        {
            var bounds = annotation.Bounds;
            var selected = new PixelRect((int)Math.Floor(bounds.X), (int)Math.Floor(bounds.Y),
                    Math.Max(1, (int)Math.Ceiling(bounds.Right) - (int)Math.Floor(bounds.X)),
                    Math.Max(1, (int)Math.Ceiling(bounds.Bottom) - (int)Math.Floor(bounds.Y)))
                .Intersect(globalRegion).Intersect(AnnotationBounds);
            var samplingBounds = sampleRegion.Intersect(AnnotationBounds);
            const int tileSize = 512;
            for (var top = selected.Y; top < selected.Bottom; top += tileSize)
            for (var left = selected.X; left < selected.Right; left += tileSize)
            {
                token.ThrowIfCancellationRequested();
                var tile = new PixelRect(left, top, Math.Min(tileSize, selected.Right - left),
                    Math.Min(tileSize, selected.Bottom - top));
                var sampling = ExpandSampleRegion(tile, samplingBounds, annotation.Properties.BlurRadius);
                var stride = checked(sampling.Width * 8);
                var pixels = new byte[checked(stride * sampling.Height)];
                for (var y = 0; y < sampling.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    samples.Pixels.Span.Slice((sampling.Y + y - sampleRegion.Y) * samples.Stride +
                                              (sampling.X - sampleRegion.X) * 8, stride)
                        .CopyTo(pixels.AsSpan(y * stride, stride));
                }

                var frame = new CapturedFrame(sampling.Width, sampling.Height, stride,
                    samples.PixelFormat, pixels, samples.ColorInfo, samples.AlphaMode, samples.Revision, []);
                var blurred = HdrPixelEffectProcessor.Apply(frame,
                    [new(PixelEffectKind.Blur, Radius: annotation.Properties.BlurRadius)], token);
                var rowBytes = tile.Width * 8;
                for (var y = 0; y < tile.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    blurred.Pixels.Span.Slice((tile.Y + y - sampling.Y) * blurred.Stride +
                                              (tile.X - sampling.X) * 8, rowBytes)
                        .CopyTo(output.AsSpan((tile.Y + y - globalRegion.Y) * destination.Stride +
                                              (tile.X - globalRegion.X) * 8, rowBytes));
                }
            }
        }
    }
}

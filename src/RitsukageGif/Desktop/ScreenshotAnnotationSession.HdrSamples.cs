namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private void RestoreHdrSamples(byte[] output, CapturedFrame destination, PixelRect globalRegion,
            CapturedFrame samples, PixelRect sampleRegion, ScreenshotAnnotation annotation, CancellationToken token)
        {
            var clip = globalRegion.Intersect(AnnotationBounds);
            if (annotation.Tool == ScreenshotTool.Eraser)
            {
                EraseHdrSamples(output, destination, globalRegion, samples, sampleRegion, annotation, token);
                return;
            }

            var bounds = annotation.Bounds;
            var area = new PixelRect((int)Math.Floor(bounds.X), (int)Math.Floor(bounds.Y),
                    Math.Max(1, (int)Math.Ceiling(bounds.Width)), Math.Max(1, (int)Math.Ceiling(bounds.Height)))
                .Intersect(clip);
            for (var y = area.Y; y < area.Bottom; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = area.X; x < area.Right; x++)
                {
                    var center = bounds.Center;
                    var sx = Math.Clamp((int)Math.Floor(center.X + (x + .5 - center.X) /
                        annotation.Properties.Magnification), sampleRegion.X, sampleRegion.Right - 1);
                    var sy = Math.Clamp((int)Math.Floor(center.Y + (y + .5 - center.Y) /
                        annotation.Properties.Magnification), sampleRegion.Y, sampleRegion.Bottom - 1);
                    samples.Pixels.Span.Slice((sy - sampleRegion.Y) * samples.Stride + (sx - sampleRegion.X) * 8, 8)
                        .CopyTo(output.AsSpan((y - globalRegion.Y) * destination.Stride + (x - globalRegion.X) * 8, 8));
                }
            }
        }
    }
}

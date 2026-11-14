using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        internal PixelRect StreamingAnnotationArea(PixelRect output)
        {
            CommitTextEdit();
            if (!AnnotationsVisible || _annotations.Count == 0) return default;
            var area = default(PixelRect);
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
            foreach (var annotation in _annotations)
            {
                var bounds = RenderBounds(annotation);
                var clipped = new PixelRect((int)Math.Floor(bounds.X), (int)Math.Floor(bounds.Y),
                        (int)Math.Ceiling(bounds.Right) - (int)Math.Floor(bounds.X),
                        (int)Math.Ceiling(bounds.Bottom) - (int)Math.Floor(bounds.Y))
                    .Intersect(AnnotationBounds).Intersect(output);
                if (clipped.IsEmpty) continue;
                area = area.IsEmpty ? clipped : Union(area, clipped);
            }

            return area.IsEmpty ? default : ExpandPixelPreviewRegion(area, _annotations).Intersect(output);
        }

        internal Task<byte[]> ComposeStreamingSdrAsync(PixelRect region, byte[] pixels, CancellationToken token)
        {
            return ComposeImageAsync(region, cancellation =>
            {
                cancellation.ThrowIfCancellationRequested();
                return new(pixels, new PixelReadSettings((uint)region.Width, (uint)region.Height,
                    StorageType.Char, PixelMapping.BGRA));
            }, token, true);
        }
    }
}

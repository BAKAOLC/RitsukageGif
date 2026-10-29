using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        public PixelRect GetPreviewBounds()
        {
            if (!AnnotationsVisible || Selection.IsEmpty) return default;
            Rect? aggregate = null;
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
            foreach (var annotation in _annotations)
            {
                var mark = RenderBounds(annotation);
                aggregate = aggregate is { } previous ? previous.Union(mark) : mark;
            }

            if (_gesture is { } gesture)
            {
                var mark = RenderBounds(gesture);
                aggregate = aggregate is { } previous ? previous.Union(mark) : mark;
            }

            if (aggregate is not { } bounds || bounds.Width <= 0 || bounds.Height <= 0) return default;
            bounds = bounds.Inflate(2).Intersect(new(AnnotationBounds.X, AnnotationBounds.Y,
                AnnotationBounds.Width, AnnotationBounds.Height));
            if (bounds.Width <= 0 || bounds.Height <= 0) return default;
            var left = (int)Math.Floor(bounds.X);
            var top = (int)Math.Floor(bounds.Y);
            return new(left, top, (int)Math.Ceiling(bounds.Right) - left, (int)Math.Ceiling(bounds.Bottom) - top);
        }
    }
}

using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private Rect LineObjectBounds(ScreenshotAnnotation annotation)
        {
            var geometry = GeometryFor(annotation);
            var bounds = geometry.Path.Bounds;
            if (geometry.StartHead is { } start) bounds = bounds.Union(start.Bounds);
            if (geometry.EndHead is { } end) bounds = bounds.Union(end.Bounds);
            if (annotation.Tool == ScreenshotTool.Arrow && !string.IsNullOrWhiteSpace(annotation.Text))
            {
                var comment = FormatLabel(annotation, Brushes.Black);
                bounds = bounds.Union(new(annotation.Points[^1] + new Vector(8, 8),
                    new Size(comment.Width, comment.Height)));
            }

            return bounds;
        }
    }
}

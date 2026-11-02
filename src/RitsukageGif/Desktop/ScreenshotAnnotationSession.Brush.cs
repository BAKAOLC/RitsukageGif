using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private AnnotationGeometryCache BrushGeometryFor(ScreenshotAnnotation annotation)
        {
            var smooth = _behavior.SmoothBrush && annotation.Points.Count > 2;
            if (annotation.GeometryCache is { } cached && cached.SmoothStroke == smooth &&
                cached.Properties == annotation.Properties && cached.Thickness.Equals(annotation.Thickness) &&
                CollectionsMarshal.AsSpan(annotation.Points).SequenceEqual(cached.Points)) return cached;
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(annotation.Points[0], false);
                for (var index = 0; index < annotation.Points.Count - 1; index++)
                {
                    var start = annotation.Points[index];
                    var end = annotation.Points[index + 1];
                    if (smooth)
                    {
                        var previous = annotation.Points[Math.Max(0, index - 1)];
                        var next = annotation.Points[Math.Min(annotation.Points.Count - 1, index + 2)];
                        path.CubicBezierTo(start + new Vector(end.X - previous.X, end.Y - previous.Y) / 6,
                            end - new Vector(next.X - start.X, next.Y - start.Y) / 6, end);
                    }
                    else
                    {
                        path.LineTo(end);
                    }
                }

                path.EndFigure(false);
            }

            var result = new AnnotationGeometryCache
            {
                Points = [.. annotation.Points], Properties = annotation.Properties,
                Thickness = annotation.Thickness, Path = geometry, SmoothStroke = smooth,
            };
            annotation.GeometryCache = result;
            return result;
        }
    }
}

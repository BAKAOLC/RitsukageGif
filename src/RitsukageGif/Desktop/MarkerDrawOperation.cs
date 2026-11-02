using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed class MarkerDrawOperation(
        Rect bounds,
        Point start,
        Point end,
        Color color,
        double width,
        double radius,
        AnnotationMarkerPath path,
        AnnotationMarkerBlend blend) : ICustomDrawOperation
    {
        public Rect Bounds { get; } = bounds;

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null) return;
            using var lease = feature.Lease();
            using var paint = new SKPaint();
            paint.IsAntialias = true;
            paint.Color = new(color.R, color.G, color.B, color.A);
            paint.BlendMode = blend == AnnotationMarkerBlend.Multiply ? SKBlendMode.Multiply : SKBlendMode.SrcOver;
            paint.StrokeWidth = (float)width;
            paint.StrokeCap = SKStrokeCap.Round;
            paint.Style = path == AnnotationMarkerPath.Rectangle ? SKPaintStyle.Fill : SKPaintStyle.Stroke;
            if (path == AnnotationMarkerPath.Rectangle)
            {
                var rectangle = new Rect(start, end).Normalize();
                lease.SkCanvas.DrawRoundRect(new((float)rectangle.X, (float)rectangle.Y,
                    (float)rectangle.Right, (float)rectangle.Bottom), (float)radius, (float)radius, paint);
            }
            else
            {
                lease.SkCanvas.DrawLine((float)start.X, (float)start.Y, (float)end.X, (float)end.Y, paint);
            }
        }

        public bool HitTest(Point point)
        {
            return Bounds.Contains(point);
        }

        public bool Equals(ICustomDrawOperation? other)
        {
            return ReferenceEquals(this, other);
        }

        public void Dispose()
        {
        }
    }
}

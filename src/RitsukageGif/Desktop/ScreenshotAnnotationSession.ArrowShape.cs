using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        internal static AnnotationGeometryCache CreateArrowShapeGeometry(ScreenshotAnnotation annotation)
        {
            var points = annotation.Points;
            var shape = annotation.Properties.ArrowShape;
            var path = new StreamGeometry();
            if (points.Count == 0)
                return CacheArrowGeometry(annotation, path);
            var start = points[0];
            var end = points[^1];
            var control = annotation.Properties.Curved ? ArrowControlPoint(annotation) : start + (end - start) / 2;
            using (var context = path.Open())
            {
                context.BeginFigure(start, false);
                if (annotation.Properties.Curved)
                    context.QuadraticBezierTo(control, end);
                else
                    context.LineTo(end);
                context.EndFigure(false);
            }

            var length = path.ContourLength;
            if (length < .01)
                return CacheArrowGeometry(annotation, path);

            var startDirection = ArrowVector(control, start);
            var endDirection = ArrowVector(control, end);
            if (startDirection.Length < .01) startDirection = ArrowVector(end, start);
            if (endDirection.Length < .01) endDirection = ArrowVector(start, end);
            var bilateral = shape is AnnotationArrowShape.StraightBilateral or AnnotationArrowShape.SolidBilateral
                or AnnotationArrowShape.DimensionArrow or AnnotationArrowShape.LineBilateral;
            var headLength = Math.Min(Math.Max(10, annotation.Thickness * 4), length / (bilateral ? 2.5 : 1.25));
            if (shape is AnnotationArrowShape.Outline or AnnotationArrowShape.Fill)
            {
                var body = CreateTaperedArrow(start, control, end, annotation.Properties.Curved,
                    annotation.Thickness, headLength);
                return CacheArrowGeometry(annotation, body, shape == AnnotationArrowShape.Fill,
                    shape == AnnotationArrowShape.Outline ? Math.Max(.75, annotation.Thickness * .4) : 0);
            }

            var open = shape is AnnotationArrowShape.Line or AnnotationArrowShape.LineBilateral;
            var ticks = shape is AnnotationArrowShape.DimensionLine or AnnotationArrowShape.DimensionArrow;
            var hasHeads = shape != AnnotationArrowShape.DimensionLine;
            var alternate = shape is AnnotationArrowShape.Solid or AnnotationArrowShape.SolidBilateral;
            Geometry? startHead = null;
            Geometry? endHead = null;
            if (bilateral || ticks)
                startHead = CreateArrowShapeHead(start, startDirection, headLength, annotation.Thickness, open, ticks,
                    hasHeads, alternate);
            if (hasHeads || ticks)
                endHead = CreateArrowShapeHead(end, endDirection, headLength, annotation.Thickness, open, ticks,
                    hasHeads, alternate);
            Geometry? shaft = null;
            if (hasHeads && !open)
            {
                var trim = headLength * (alternate ? .9 : .65);
                if (path.TryGetSegment(bilateral ? trim : 0, length - trim, true, out var segment))
                    shaft = segment;
            }

            var result = new AnnotationGeometryCache
            {
                Points = [.. points],
                Properties = annotation.Properties,
                Thickness = annotation.Thickness,
                Path = path,
                Shaft = shaft,
                StartHead = startHead,
                EndHead = endHead,
                StartFilled = hasHeads && !open,
                EndFilled = hasHeads && !open,
            };
            annotation.GeometryCache = result;
            return result;
        }

        internal static void DrawArrowShapeGeometry(DrawingContext context, AnnotationGeometryCache geometry,
            IBrush brush, IPen pen)
        {
            if (geometry.OutlineWidth > 0)
            {
                if (!ArrowBrushMatches(geometry.ArrowOutlinePen?.Brush, brush))
                    geometry.ArrowOutlinePen = new Pen(brush, geometry.OutlineWidth, pen.DashStyle,
                        PenLineCap.Round, PenLineJoin.Round);
                context.DrawGeometry(null, geometry.ArrowOutlinePen, geometry.Path);
                return;
            }

            context.DrawGeometry(geometry.PathFilled ? brush : null, geometry.PathFilled ? null : pen,
                geometry.Shaft ?? geometry.Path);
            if (geometry.StartHead is { } start)
                context.DrawGeometry(geometry.StartFilled ? brush : null, geometry.StartFilled ? null : pen, start);
            if (geometry.EndHead is { } end)
                context.DrawGeometry(geometry.EndFilled ? brush : null, geometry.EndFilled ? null : pen, end);
        }

        private static bool ArrowBrushMatches(IBrush? cached, IBrush current)
        {
            return ReferenceEquals(cached, current) ||
                   (cached is ISolidColorBrush first && current is ISolidColorBrush second &&
                    first.Color == second.Color && first.Opacity.Equals(second.Opacity));
        }

        private static Vector ArrowVector(Point from, Point to)
        {
            return new(to.X - from.X, to.Y - from.Y);
        }

        private static AnnotationGeometryCache CacheArrowGeometry(ScreenshotAnnotation annotation, Geometry path,
            bool filled = false, double outlineWidth = 0)
        {
            var result = new AnnotationGeometryCache
            {
                Points = [.. annotation.Points],
                Properties = annotation.Properties,
                Thickness = annotation.Thickness,
                Path = path,
                PathFilled = filled,
                OutlineWidth = outlineWidth,
            };
            annotation.GeometryCache = result;
            return result;
        }

        private static StreamGeometry CreateArrowShapeHead(Point tip, Vector direction, double length, double thickness,
            bool open, bool ticks, bool hasHead, bool alternate)
        {
            var path = new StreamGeometry();
            if (direction.Length < .01) return path;
            var unit = direction / direction.Length;
            var normal = new Vector(-unit.Y, unit.X);
            var back = tip - unit * length;
            using var context = path.Open();
            context.SetFillRule(FillRule.NonZero);
            if (hasHead)
            {
                context.BeginFigure(back + normal * (length / 2), !open);
                context.LineTo(tip);
                context.LineTo(back - normal * (length / 2));
                if (!open && !alternate) context.LineTo(tip - unit * (length * .65));
                context.EndFigure(!open);
            }

            if (ticks)
            {
                if (hasHead)
                {
                    var halfWidth = thickness / 2;
                    context.BeginFigure(tip + normal * (length * .65) + unit * halfWidth);
                    context.LineTo(tip - normal * (length * .65) + unit * halfWidth);
                    context.LineTo(tip - normal * (length * .65) - unit * halfWidth);
                    context.LineTo(tip + normal * (length * .65) - unit * halfWidth);
                    context.EndFigure(true);
                }
                else
                {
                    context.BeginFigure(tip + normal * (length * .65), false);
                    context.LineTo(tip - normal * (length * .65));
                    context.EndFigure(false);
                }
            }

            return path;
        }

        private static StreamGeometry CreateTaperedArrow(Point start, Point control, Point end, bool curved,
            double thickness, double headLength)
        {
            var divisions = curved
                ? Math.Clamp(
                    (int)Math.Ceiling((ArrowVector(start, control).Length + ArrowVector(control, end).Length) / 6), 8,
                    256)
                : 1;
            var samples = new Point[divisions + 1];
            var distances = new double[divisions + 1];
            samples[0] = start;
            for (var index = 1; index <= divisions; index++)
            {
                var fraction = (double)index / divisions;
                samples[index] = curved
                    ? new((1 - fraction) * (1 - fraction) * start.X +
                          2 * (1 - fraction) * fraction * control.X + fraction * fraction * end.X,
                        (1 - fraction) * (1 - fraction) * start.Y +
                        2 * (1 - fraction) * fraction * control.Y + fraction * fraction * end.Y)
                    : end;
                distances[index] = distances[index - 1] + ArrowVector(samples[index - 1], samples[index]).Length;
            }

            var bodyLength = Math.Max(0, distances[^1] - headLength);
            var bodyEndIndex = 1;
            while (bodyEndIndex < divisions && distances[bodyEndIndex] < bodyLength) bodyEndIndex++;
            var segmentLength = distances[bodyEndIndex] - distances[bodyEndIndex - 1];
            var fractionAlong = segmentLength > 0
                ? (bodyLength - distances[bodyEndIndex - 1]) / segmentLength
                : 0;
            var bodyEnd = samples[bodyEndIndex - 1] +
                          (samples[bodyEndIndex] - samples[bodyEndIndex - 1]) * fractionAlong;
            var finalDirection = ArrowVector(control, end);
            if (finalDirection.Length < .01) finalDirection = ArrowVector(start, end);
            var finalNormal = new Vector(-finalDirection.Y, finalDirection.X) / finalDirection.Length;
            var neckWidth = Math.Min(thickness, headLength / 3);
            var result = new StreamGeometry();
            using var context = result.Open();
            context.BeginFigure(start);
            for (var index = 1; index < bodyEndIndex; index++)
                context.LineTo(ArrowBodyEdge(samples, distances, index, bodyLength, neckWidth, 1));
            context.LineTo(bodyEnd + finalNormal * neckWidth);
            context.LineTo(bodyEnd + finalNormal * (headLength / 2));
            context.LineTo(end);
            context.LineTo(bodyEnd - finalNormal * (headLength / 2));
            context.LineTo(bodyEnd - finalNormal * neckWidth);
            for (var index = bodyEndIndex - 1; index > 0; index--)
                context.LineTo(ArrowBodyEdge(samples, distances, index, bodyLength, neckWidth, -1));
            context.EndFigure(true);
            return result;
        }

        private static Point ArrowBodyEdge(Point[] samples, double[] distances, int index, double length,
            double width, int side)
        {
            var tangent = ArrowVector(samples[index - 1], samples[index + 1]);
            if (tangent.Length < .01) tangent = ArrowVector(samples[index - 1], samples[index]);
            if (tangent.Length < .01) return samples[index];
            var normal = new Vector(-tangent.Y, tangent.X) / tangent.Length;
            return samples[index] + normal * (width * side * distances[index] / length);
        }
    }
}

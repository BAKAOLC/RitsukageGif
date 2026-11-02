using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        public bool CanStraightenCurveAt(PixelPoint point)
        {
            return !IsBusy && !IsDrawing && _selected is
                       { Properties.Curved: true, Tool: ScreenshotTool.Arrow or ScreenshotTool.Line } annotation &&
                   CanEditControlPoints(annotation) && _behavior.ArrowCurveControlPoints &&
                   Distance(ArrowControlPoint(annotation), new(point.X, point.Y)) <= ControlHitRadius;
        }

        private static void NormalizeGeometryPoints(ScreenshotAnnotation annotation)
        {
            if (annotation.Tool is not (ScreenshotTool.Arrow or ScreenshotTool.Line) || annotation.Points.Count < 2)
                return;
            if (annotation.Properties.Curved)
            {
                if (annotation.Points.Count == 2)
                    annotation.Points.Insert(1, ArrowControlPoint(annotation));
            }
            else if (annotation.Points.Count > 2)
            {
                annotation.Points.RemoveRange(1, annotation.Points.Count - 2);
            }
        }

        private static AnnotationGeometryCache GeometryFor(ScreenshotAnnotation annotation)
        {
            if (annotation.GeometryCache is { } cached && cached.Properties == annotation.Properties &&
                cached.Thickness.Equals(annotation.Thickness) &&
                CollectionsMarshal.AsSpan(annotation.Points).SequenceEqual(cached.Points))
                return cached;

            if (annotation.Tool == ScreenshotTool.Arrow)
            {
                var arrow = CreateArrowShapeGeometry(annotation);
                annotation.GeometryCache = arrow;
                return arrow;
            }

            var points = annotation.Points;
            var properties = annotation.Properties;
            Geometry geometry;
            Geometry? shaft = null;
            Geometry? startHead = null, endHead = null;
            var startFilled = false;
            var endFilled = false;
            if (annotation.Tool is ScreenshotTool.Rectangle or ScreenshotTool.Ellipse or ScreenshotTool.Spotlight)
            {
                var bounds = annotation.Bounds;
                var radius = Math.Min(properties.CornerRadius, Math.Min(bounds.Width, bounds.Height) / 2);
                geometry = (annotation.Tool is ScreenshotTool.Rectangle or ScreenshotTool.Ellipse &&
                            properties.GeometryShape == AnnotationGeometryShape.Ellipse) ||
                           (annotation.Tool == ScreenshotTool.Spotlight && properties.SpotlightEllipse)
                    ? new EllipseGeometry(bounds)
                    : new RectangleGeometry(bounds, radius, radius);
            }
            else
            {
                var path = new StreamGeometry();
                using (var context = path.Open())
                {
                    context.BeginFigure(points[0],
                        annotation.Tool is ScreenshotTool.Line or ScreenshotTool.Polyline &&
                        properties.StrokeStyle == AnnotationStrokeStyle.Filled);
                    switch (properties.Curved)
                    {
                        case true when annotation.Tool is ScreenshotTool.Arrow or ScreenshotTool.Line:
                            context.QuadraticBezierTo(ArrowControlPoint(annotation), points[^1]);
                            break;
                        case true when points.Count > 2:
                            for (var index = 0; index < points.Count - 1; index++)
                            {
                                var previous = points[Math.Max(0, index - 1)];
                                var start = points[index];
                                var end = points[index + 1];
                                var next = points[Math.Min(points.Count - 1, index + 2)];
                                context.CubicBezierTo(start + new Vector(end.X - previous.X, end.Y - previous.Y) / 6,
                                    end - new Vector(next.X - start.X, next.Y - start.Y) / 6, end);
                            }

                            break;
                        default:
                            for (var index = 1; index < points.Count; index++)
                                context.LineTo(points[index]);
                            break;
                    }

                    context.EndFigure(annotation.Tool is ScreenshotTool.Line or ScreenshotTool.Polyline &&
                                      properties.StrokeStyle == AnnotationStrokeStyle.Filled);
                }

                geometry = path;
                if (points.Count > 1 && properties.StrokeStyle != AnnotationStrokeStyle.Filled)
                {
                    var startAdjacent =
                        properties.Curved && annotation.Tool is ScreenshotTool.Arrow or ScreenshotTool.Line
                            ? ArrowControlPoint(annotation)
                            : points[1];
                    var endAdjacent =
                        properties.Curved && annotation.Tool is ScreenshotTool.Arrow or ScreenshotTool.Line
                            ? ArrowControlPoint(annotation)
                            : points[^2];
                    (startHead, startFilled) = EndpointGeometry(properties.StartEndpoint,
                        points[0], new(points[0].X - startAdjacent.X, points[0].Y - startAdjacent.Y),
                        annotation.Thickness);
                    (endHead, endFilled) = EndpointGeometry(properties.EndEndpoint,
                        points[^1], new(points[^1].X - endAdjacent.X, points[^1].Y - endAdjacent.Y),
                        annotation.Thickness);
                    var length = geometry.ContourLength;
                    var startTrim = EndpointTrim(properties.StartEndpoint, annotation.Thickness);
                    var endTrim = EndpointTrim(properties.EndEndpoint, annotation.Thickness);
                    var totalTrim = startTrim + endTrim;
                    if (totalTrim > 0 && length > 0)
                    {
                        var scale = Math.Min(1, Math.Max(0, length - annotation.Thickness * 2) / totalTrim);
                        if (geometry.TryGetSegment(startTrim * scale, length - endTrim * scale, true,
                                out var segment)) shaft = segment;
                    }
                }
            }

            var result = new AnnotationGeometryCache
            {
                Points = [.. points], Properties = properties, Thickness = annotation.Thickness, Path = geometry,
                Shaft = shaft,
                StartHead = startHead, EndHead = endHead, StartFilled = startFilled, EndFilled = endFilled,
            };
            annotation.GeometryCache = result;
            return result;
        }

        internal static double EndpointTrim(AnnotationEndpointStyle style, double thickness)
        {
            return style switch
            {
                AnnotationEndpointStyle.SolidArrow or AnnotationEndpointStyle.UnfilledArrow or
                    AnnotationEndpointStyle.SolidDot or AnnotationEndpointStyle.OpenCircle or
                    AnnotationEndpointStyle.SolidDiamond or AnnotationEndpointStyle.OpenDiamond =>
                    Math.Max(8, thickness * 3),
                AnnotationEndpointStyle.ClosedFilledArrow => Math.Max(8, thickness * 3) * .8,
                AnnotationEndpointStyle.NotchedArrow => Math.Max(8, thickness * 3) * (2d / 3),
                _ => 0,
            };
        }

        internal static (Geometry? Geometry, bool Filled) EndpointGeometry(AnnotationEndpointStyle style,
            Point tip, Vector direction, double thickness)
        {
            if (style == AnnotationEndpointStyle.None || direction.Length < .01) return (null, false);
            var unit = direction / direction.Length;
            var side = new Vector(-unit.Y, unit.X);
            var length = Math.Max(8, thickness * 3);
            var back = tip - unit * length;
            var center = tip - unit * (length / 2);
            if (style is AnnotationEndpointStyle.SolidDot or AnnotationEndpointStyle.OpenCircle)
                return (new EllipseGeometry(new(center - new Vector(length / 2, length / 2),
                        new Size(length, length))),
                    style == AnnotationEndpointStyle.SolidDot);
            var path = new StreamGeometry();
            var filled = style is AnnotationEndpointStyle.SolidArrow or AnnotationEndpointStyle.SolidDiamond
                or AnnotationEndpointStyle.ClosedFilledArrow or AnnotationEndpointStyle.NotchedArrow;
            using (var context = path.Open())
                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                switch (style)
                {
                    case AnnotationEndpointStyle.LineArrow:
                    case AnnotationEndpointStyle.OpenArrow:
                        context.BeginFigure(back + side * (length / 2), false);
                        context.LineTo(tip);
                        context.LineTo(back - side * (length / 2));
                        context.EndFigure(false);
                        break;
                    case AnnotationEndpointStyle.SolidArrow:
                    case AnnotationEndpointStyle.UnfilledArrow:
                        context.BeginFigure(tip, filled);
                        context.LineTo(back + side * (length / 2));
                        context.LineTo(back - side * (length / 2));
                        context.EndFigure(true);
                        break;
                    case AnnotationEndpointStyle.SolidDiamond:
                    case AnnotationEndpointStyle.OpenDiamond:
                        context.BeginFigure(tip, filled);
                        context.LineTo(center + side * (length / 2));
                        context.LineTo(back);
                        context.LineTo(center - side * (length / 2));
                        context.EndFigure(true);
                        break;
                    case AnnotationEndpointStyle.ArchitecturalTick:
                        context.BeginFigure(tip - (unit + side) * (length / 2), false);
                        context.LineTo(tip + (unit + side) * (length / 2));
                        context.EndFigure(false);
                        break;
                    case AnnotationEndpointStyle.ClosedFilledArrow:
                    case AnnotationEndpointStyle.NotchedArrow:
                        context.BeginFigure(tip);
                        context.LineTo(back + side * (length / 2));
                        context.LineTo(tip -
                                       unit * (length * (style == AnnotationEndpointStyle.NotchedArrow ? 2d / 3 : .8)));
                        context.LineTo(back - side * (length / 2));
                        context.EndFigure(true);
                        break;
                    case AnnotationEndpointStyle.Cross:
                        context.BeginFigure(tip - (unit + side) * (length / 2), false);
                        context.LineTo(tip + (unit + side) * (length / 2));
                        context.EndFigure(false);
                        context.BeginFigure(tip - (unit - side) * (length / 2), false);
                        context.LineTo(tip + (unit - side) * (length / 2));
                        context.EndFigure(false);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(style));
                }

            return (path, filled);
        }

        private static void DrawArrowShape(DrawingContext context, ScreenshotAnnotation annotation,
            IBrush brush, IPen pen)
        {
            DrawArrowShapeGeometry(context, GeometryFor(annotation), brush, pen);
        }

        private static void DrawLineGeometry(DrawingContext context, ScreenshotAnnotation annotation,
            IBrush brush, IPen pen)
        {
            var geometry = GeometryFor(annotation);
            var filled = annotation.Properties.StrokeStyle == AnnotationStrokeStyle.Filled;
            context.DrawGeometry(filled ? brush : null, filled ? null : pen, geometry.Shaft ?? geometry.Path);
            var endpointPen = annotation.Properties.StrokeStyle != AnnotationStrokeStyle.Solid
                ? new Pen(brush, annotation.Thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round)
                : pen;
            if (geometry.StartHead is { } start)
                context.DrawGeometry(geometry.StartFilled ? brush : null,
                    annotation.Properties.StartEndpoint == AnnotationEndpointStyle.OpenArrow
                        ? new Pen(brush, annotation.Thickness / 2, lineCap: PenLineCap.Square)
                        : endpointPen, start);
            if (geometry.EndHead is { } end)
                context.DrawGeometry(geometry.EndFilled ? brush : null,
                    annotation.Properties.EndEndpoint == AnnotationEndpointStyle.OpenArrow
                        ? new Pen(brush, annotation.Thickness / 2, lineCap: PenLineCap.Square)
                        : endpointPen, end);
        }

        private void DrawSpotlightGeometry(DrawingContext context, ScreenshotAnnotation annotation)
        {
            var whole = new Rect(AnnotationBounds.X, AnnotationBounds.Y, AnnotationBounds.Width,
                AnnotationBounds.Height);
            var geometry = GeometryFor(annotation);
            if (geometry.Shade is null || geometry.ShadeBounds != whole)
            {
                geometry.ShadeBounds = whole;
                geometry.Shade = new CombinedGeometry(GeometryCombineMode.Exclude,
                    new RectangleGeometry(whole), geometry.Path);
            }

            using (context.PushOpacity(annotation.Properties.Opacity))
                context.DrawGeometry(Brushes.Black, null, geometry.Shade);
            if (annotation.Properties.SpotlightStroke)
                context.DrawGeometry(null, new Pen(new SolidColorBrush(annotation.Color), annotation.Thickness),
                    geometry.Path);
        }
    }
}

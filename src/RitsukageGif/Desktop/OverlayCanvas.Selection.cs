using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayCanvas
    {
        private const double SelectionHandleRadius = 4;
        private static readonly Pen HandleBorder = new(Brushes.White, 1.5);
        private RectangleGeometry? _screenshotRoundedOutline;

        internal double? SelectionMarkerScale { get; set; }

        private bool IsScreenshotRectangle => IsScreenshot && Snapshot is
            { Shape: null, ShapeMode: SelectionShapeMode.Rectangle };

        private double ConfiguredBorderWidth =>
            _appearance?.SelectionBorderWidth ?? AppearancePreferences.DefaultSelectionBorderWidth;

        private double ScreenshotMarkerRadius => 5 * (SelectionMarkerScale ?? Scale) / Scale;

        private bool SelectionHandlesVisible => (Snapshot?.Phase == SelectionPhase.Selected || Manipulating) &&
                                                _appearance?.SelectionAnchors != SelectionAnchorMode.None;

        private void DrawWindowFrame(DrawingContext context, PixelRect region)
        {
            if (WindowFrame is not { } frame) return;
            var intersection = region.Intersect(frame.Region);
            if (intersection.IsEmpty) return;
            var local = intersection.ToLocal(_monitor, Scale);
            context.DrawImage(frame.Bitmap,
                new(intersection.X - frame.Region.X, intersection.Y - frame.Region.Y, intersection.Width,
                    intersection.Height),
                new(local.X, local.Y, local.Width, local.Height));
        }

        private void DrawSelectionBorder(DrawingContext context, PixelRect intersection, Rect destination)
        {
            if (Selected.IsEmpty || (!IsScreenshotRectangle && intersection.IsEmpty)) return;
            var (geometry, pen) = SelectionBorder();
            if (pen is null) return;
            using var opacity = context.PushOpacity(SelectionBorderOpacity());
            if (IsScreenshotRectangle)
            {
                using var clip = context.PushClip(new(0, 0, _monitor.Width / Scale, _monitor.Height / Scale));
                if (geometry is not null) context.DrawGeometry(null, pen, geometry);
                else context.DrawRectangle(null, pen, LocalSelectionRect().Inflate(pen.Thickness / 2));
                return;
            }

            if (geometry is not null)
            {
                context.DrawGeometry(null, pen, geometry);
                return;
            }

            // Draw only real selection edges when the rectangle spans monitors.
            if (intersection.X == Selected.X)
                context.DrawLine(pen, destination.TopLeft, destination.BottomLeft);
            if (intersection.Right == Selected.Right)
                context.DrawLine(pen, destination.TopRight, destination.BottomRight);
            if (intersection.Y == Selected.Y)
                context.DrawLine(pen, destination.TopLeft, destination.TopRight);
            if (intersection.Bottom == Selected.Bottom)
                context.DrawLine(pen, destination.BottomLeft, destination.BottomRight);
        }

        private (Geometry? Geometry, IPen? Pen) SelectionBorder()
        {
            var pen = IsCandidate && (!IsScreenshot || Snapshot is null) ? _candidatePen : _selectedPen;
            var shape = SelectionGeometry();
            if (shape is not null) return (shape, pen);

            // Keep a thin outline when the rounded frame has less than ten pixels of straight edge.
            var radius = IsCandidate ? 0 : FrameRadiusPixels;
            var smallerSide = Math.Min(Selected.Width, Selected.Height);
            var width = ConfiguredBorderWidth;
            if (width == 0 && (radius <= 0 || radius <= (smallerSide - 10) / 2d)) return (null, null);
            if (IsScreenshotRectangle && radius > 0)
            {
                var outline = _screenshotRoundedOutline ??= new();
                outline.Rect = LocalSelectionRect().Inflate(pen.Thickness / 2);
                outline.RadiusX = outline.RadiusY = radius / Scale;
                return (outline, pen);
            }

            if (width > 0) return (null, pen);
            PrepareFrameDecoration();
            return (_frameClip, pen);
        }

        private Rect LocalSelectionRect()
        {
            var local = Selected.ToLocal(_monitor, Scale);
            return new(local.X, local.Y, local.Width, local.Height);
        }

        private double SelectionScenePadding(IPen? pen, bool handles)
        {
            var pad = (pen?.Thickness ?? 0) / 2;
            if (IsScreenshotRectangle)
            {
                pad += pad;
                if (handles)
                {
                    var radius = ScreenshotMarkerRadius;
                    pad = Math.Max(pad, ConfiguredBorderWidth / (2 * Scale) + radius + Math.Max(1 / Scale, radius / 3));
                }
            }
            else if (handles)
            {
                pad = Math.Max(pad, SelectionHandleRadius + HandleBorder.Thickness / 2);
            }

            return pad * Scale + 1;
        }

        private void DrawSelectionHandles(DrawingContext context)
        {
            if (Selected.IsEmpty || !SelectionHandlesVisible) return;
            var area = LocalSelectionRect();
            using var clip = context.PushClip(new(Bounds.Size));
            if (IsScreenshotRectangle)
            {
                DrawScreenshotSelectionMarkers(context, area.Inflate(ConfiguredBorderWidth / (2 * Scale)));
                return;
            }

            Span<Point> points =
            [
                area.TopLeft, new(area.Center.X, area.Top), area.TopRight, new(area.Right, area.Center.Y),
                area.BottomRight, new(area.Center.X, area.Bottom), area.BottomLeft, new(area.Left, area.Center.Y),
            ];
            for (var index = 0; index < points.Length; index++)
            {
                if (_appearance?.SelectionAnchors == SelectionAnchorMode.Four && index % 2 != 0) continue;
                context.DrawEllipse(_selectionBrush, HandleBorder, points[index], SelectionHandleRadius,
                    SelectionHandleRadius);
            }
        }

        private void DrawScreenshotSelectionMarkers(DrawingContext context, Rect area)
        {
            var radius = ScreenshotMarkerRadius;
            var size = Math.Min(area.Width, area.Height);
            if (FrameRadiusPixels == 0 && size >= 4 * radius + 10 / Scale)
            {
                var outline = radius + Math.Max(1 / Scale, radius / 3);
                DrawMarker(area.TopLeft, outline);
                DrawMarker(area.TopRight, outline);
                DrawMarker(area.BottomRight, outline);
                DrawMarker(area.BottomLeft, outline);
            }

            if (_appearance?.SelectionAnchors == SelectionAnchorMode.Four || size < 2 * radius ||
                Math.Min(Selected.Width, Selected.Height) <= ConfiguredBorderWidth) return;
            var edgeOutline = radius + Math.Max(1 / Scale, radius / 4);
            DrawMarker(new(area.Center.X, area.Top), edgeOutline);
            DrawMarker(new(area.Center.X, area.Bottom), edgeOutline);
            DrawMarker(new(area.Left, area.Center.Y), edgeOutline);
            DrawMarker(new(area.Right, area.Center.Y), edgeOutline);
            return;

            void DrawMarker(Point point, double outline)
            {
                context.DrawEllipse(Brushes.White, null, point, outline, outline);
                context.DrawEllipse(_selectionBrush, null, point, radius, radius);
            }
        }
    }
}

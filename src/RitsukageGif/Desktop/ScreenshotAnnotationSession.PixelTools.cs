using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        public async Task SetPixelEffectModeAsync(ScreenshotTool tool)
        {
            if (tool is not (ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.SmartErase))
                throw new ArgumentOutOfRangeException(nameof(tool));
            if (_disposed || !CanAnnotate || IsBusy || IsDrawing) return;
            if (_selected is not
                { Tool: ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.SmartErase } annotation)
            {
                Tool = tool;
                return;
            }

            if (annotation.Tool == tool) return;
            var replacement = CloneAnnotation(annotation, tool: tool);
            await ReplaceAnnotationAsync(_annotations.IndexOf(annotation), replacement);
            if (_disposed || !ReferenceEquals(_selected, replacement)) return;
            if (_tool is ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.SmartErase) _tool = tool;
            _styleTool = tool;
            StoreToolStyle(tool);
            Changed?.Invoke();
        }

        private static bool IsPixelTool(ScreenshotTool tool)
        {
            return tool is ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.SmartErase
                or ScreenshotTool.Eraser;
        }

        private static bool IsPixelBrush(ScreenshotAnnotation annotation)
        {
            return IsPixelTool(annotation.Tool) && annotation.Properties.PixelPath == AnnotationPixelPath.Brush;
        }

        private static Rect PixelEffectBounds(ScreenshotAnnotation annotation)
        {
            if (!IsPixelBrush(annotation)) return annotation.Bounds;
            var left = annotation.Points.Min(point => point.X);
            var top = annotation.Points.Min(point => point.Y);
            var right = annotation.Points.Max(point => point.X);
            var bottom = annotation.Points.Max(point => point.Y);
            return new Rect(left, top, right - left, bottom - top).Inflate(annotation.Thickness / 2 + 1);
        }

        private static Geometry PixelGeometryFor(ScreenshotAnnotation annotation)
        {
            if (annotation.GeometryCache is { } cached && cached.Properties == annotation.Properties &&
                cached.Thickness.Equals(annotation.Thickness) &&
                CollectionsMarshal.AsSpan(annotation.Points).SequenceEqual(cached.Points)) return cached.Path;
            Geometry geometry;
            if (IsPixelBrush(annotation))
            {
                if (annotation.Points.Count == 1)
                {
                    var center = annotation.Points[0];
                    var radius = annotation.Thickness / 2;
                    geometry = new EllipseGeometry(new(center.X - radius, center.Y - radius,
                        annotation.Thickness, annotation.Thickness));
                }
                else
                {
                    var path = new StreamGeometry();
                    using (var context = path.Open())
                    {
                        context.BeginFigure(annotation.Points[0], false);
                        for (var index = 1; index < annotation.Points.Count; index++)
                            context.LineTo(annotation.Points[index]);
                        context.EndFigure(false);
                    }

                    geometry = path.GetWidenedGeometry(new Pen(Brushes.Black, annotation.Thickness,
                        lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round));
                }
            }
            else
            {
                var bounds = annotation.Bounds;
                var radius = Math.Min(annotation.Properties.CornerRadius, Math.Min(bounds.Width, bounds.Height) / 2);
                geometry = new RectangleGeometry(bounds, radius, radius);
            }

            annotation.GeometryCache = new()
            {
                Points = [.. annotation.Points], Properties = annotation.Properties,
                Thickness = annotation.Thickness, Path = geometry,
            };
            return geometry;
        }

        private static void DrawPixelTool(DrawingContext context, ScreenshotAnnotation annotation)
        {
            var geometry = PixelGeometryFor(annotation);
            if (annotation.ImageTiles.Count == 0)
            {
                context.DrawGeometry(null, new Pen(Brushes.DeepSkyBlue, 1, DashStyle.Dash), geometry);
                return;
            }

            using var clip = context.PushGeometryClip(geometry);
            foreach (var tile in annotation.ImageTiles) context.DrawImage(tile.Image, tile.Bounds);
        }

        private static ushort[] CreatePixelCoverage(ScreenshotAnnotation annotation, PixelRect region,
            CancellationToken token)
        {
            if (IsPixelBrush(annotation)) return CreateBrushCoverage(annotation, region, token);
            var coverage = new ushort[checked(region.Width * region.Height)];
            var bounds = annotation.Bounds;
            var radius = Math.Min(annotation.Properties.CornerRadius, Math.Min(bounds.Width, bounds.Height) / 2);
            for (var y = 0; y < region.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < region.Width; x++)
                {
                    var px = region.X + x;
                    var py = region.Y + y;
                    if (Contains(px, py) && Contains(px + 1, py) && Contains(px, py + 1) && Contains(px + 1, py + 1))
                    {
                        coverage[y * region.Width + x] = ushort.MaxValue;
                        continue;
                    }

                    for (var subY = 0; subY < 4; subY++)
                    for (var subX = 0; subX < 4; subX++)
                        if (Contains(px + (subX + .5) / 4, py + (subY + .5) / 4))
                            coverage[y * region.Width + x] |= (ushort)(1 << (subY * 4 + subX));
                }
            }

            return coverage;

            bool Contains(double x, double y)
            {
                if (x < bounds.Left || x > bounds.Right || y < bounds.Top || y > bounds.Bottom) return false;
                if (radius == 0) return true;
                var cx = Math.Clamp(x, bounds.Left + radius, bounds.Right - radius);
                var cy = Math.Clamp(y, bounds.Top + radius, bounds.Bottom - radius);
                return (x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius;
            }
        }
    }
}

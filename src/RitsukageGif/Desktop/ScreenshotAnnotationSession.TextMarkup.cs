using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        internal void SetTextMarkup(ImageTextMarkup markup, double pixelRatio)
        {
            foreach (var annotation in _annotations) annotation.Dispose();
            _annotations.Clear();
            foreach (var (kind, color, bounds) in markup.Runs())
            {
                var thickness = bounds.Height * .08;
                var annotation = new ScreenshotAnnotation
                {
                    Tool = kind == ImageTextMarkKind.Highlight ? ScreenshotTool.Highlight : ScreenshotTool.Brush,
                    Color = color, Thickness = thickness,
                    Properties = new() { MarkerPath = AnnotationMarkerPath.Rectangle },
                    FixedStroke = kind == ImageTextMarkKind.Highlight
                        ? null
                        : new(
                            TextMarkGeometry(kind, bounds, thickness, pixelRatio),
                            new Pen(new SolidColorBrush(color), thickness,
                                lineCap: kind == ImageTextMarkKind.WavyUnderline
                                    ? PenLineCap.Square
                                    : PenLineCap.Round),
                            bounds),
                };
                annotation.Points.Add(bounds.TopLeft);
                annotation.Points.Add(bounds.BottomRight);
                _annotations.Add(annotation);
            }

            Changed?.Invoke();
        }

        private static StreamGeometry TextMarkGeometry(ImageTextMarkKind kind, Rect bounds,
            double thickness, double pixelRatio)
        {
            var path = new StreamGeometry();
            using var context = path.Open();
            var baseline = kind == ImageTextMarkKind.Strikethrough ? bounds.Center.Y : bounds.Bottom - thickness / 2;
            if (kind != ImageTextMarkKind.WavyUnderline)
            {
                context.BeginFigure(new(bounds.Left, baseline), false);
                context.LineTo(new(bounds.Right, baseline));
            }
            else
            {
                baseline -= pixelRatio;
                var period = 6 * pixelRatio;
                var up = true;
                context.BeginFigure(new(bounds.Left, baseline), false);
                for (var x = bounds.Left; x <= bounds.Right; x += period)
                {
                    context.QuadraticBezierTo(new(x + period / 2, baseline + (up ? -pixelRatio : pixelRatio)),
                        new(x + period, baseline));
                    up = !up;
                }
            }

            context.EndFigure(false);
            return path;
        }
    }
}

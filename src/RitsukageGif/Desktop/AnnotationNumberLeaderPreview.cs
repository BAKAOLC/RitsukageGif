using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class AnnotationNumberLeaderPreview : Control
    {
        public AnnotationNumberLeaderPreview()
        {
            Width = 34;
            Height = 22;
        }

        public AnnotationEndpointStyle Style { get; set; }
        public bool Callout { get; set; }

        public override void Render(DrawingContext context)
        {
            var brush = AppTheme.Ink;
            context.DrawEllipse(brush, null, new(6, 11), 4, 4);
            if (Callout)
            {
                var shape = new StreamGeometry();
                using (var path = shape.Open())
                {
                    path.BeginFigure(new(7, 7));
                    path.LineTo(new(31, 11));
                    path.LineTo(new(7, 15));
                    path.EndFigure(true);
                }

                context.DrawGeometry(brush, null, shape);
                return;
            }

            var pen = new Pen(brush, 1.5);
            var (head, filled) = ScreenshotAnnotationSession.EndpointGeometry(Style, new(30, 11), new(1, 0), 1.5);
            var trim = ScreenshotAnnotationSession.EndpointTrim(Style, 1.5);
            context.DrawLine(pen, new(10, 11), new(30 - trim, 11));
            if (head is not null) context.DrawGeometry(filled ? brush : null, pen, head);
        }
    }
}

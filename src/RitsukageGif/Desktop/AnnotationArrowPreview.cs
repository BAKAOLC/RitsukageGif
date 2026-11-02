using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class AnnotationArrowPreview : Control
    {
        private IBrush? _brush;
        private AnnotationGeometryCache? _geometry;
        private Pen? _pen;

        public AnnotationArrowPreview()
        {
            Width = 54;
            Height = 22;
        }

        public AnnotationArrowShape Shape
        {
            set
            {
                if (field == value) return;
                field = value;
                var properties = new ScreenshotAnnotationProperties { ArrowShape = value };
                using var annotation = new ScreenshotAnnotation
                {
                    Tool = ScreenshotTool.Arrow, Color = Colors.Black, Thickness = 2,
                    Properties = properties,
                };
                annotation.Points.Add(new(5, 11));
                annotation.Points.Add(new(49, 11));
                _geometry = ScreenshotAnnotationSession.CreateArrowShapeGeometry(annotation);
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (_geometry is null) return;
            var brush = AppTheme.Ink;
            if (_pen is null || !ReferenceEquals(_brush, brush))
            {
                _brush = brush;
                _pen = new(brush, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            }

            ScreenshotAnnotationSession.DrawArrowShapeGeometry(context, _geometry, brush, _pen);
        }
    }
}

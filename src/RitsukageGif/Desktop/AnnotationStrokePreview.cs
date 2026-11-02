using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class AnnotationStrokePreview : Control
    {
        private IBrush? _brush;
        private Pen? _pen;

        public AnnotationStrokePreview()
        {
            Width = 48;
            Height = 22;
        }

        public AnnotationStrokeStyle Style
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                _pen = null;
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var brush = AppTheme.Ink;
            if (_pen is null || !ReferenceEquals(_brush, brush))
            {
                _brush = brush;
                _pen = new(brush, 1.5, AnnotationStroke.Dashes(Style));
            }

            if (Style == AnnotationStrokeStyle.Filled)
                context.DrawRectangle(brush, null, new(5, 6, 38, 10));
            else
                context.DrawLine(_pen, new(3, 11), new(45, 11));
        }
    }
}

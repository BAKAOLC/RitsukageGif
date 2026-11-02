using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class AnnotationEndpointPreview : Control
    {
        private bool _filled;
        private Geometry? _head;

        public AnnotationEndpointPreview()
        {
            Width = 34;
            Height = 22;
        }

        public bool Start { get; init; }

        public AnnotationEndpointStyle Style
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                (_head, _filled) = ScreenshotAnnotationSession.EndpointGeometry(value,
                    new(29, 11), new(1, 0), 2);
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            using var flip = context.PushTransform(Start ? new(-1, 0, 0, 1, 34, 0) : Matrix.Identity);
            var pen = new Pen(AppTheme.Ink, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawLine(pen, new(4, 11), new(29 - ScreenshotAnnotationSession.EndpointTrim(Style, 2), 11));
            if (_head is not null)
                context.DrawGeometry(_filled ? AppTheme.Ink : null,
                    Style == AnnotationEndpointStyle.OpenArrow ? new(AppTheme.Ink, .75) : pen, _head);
        }
    }
}

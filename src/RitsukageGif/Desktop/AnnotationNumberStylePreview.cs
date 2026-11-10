using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class AnnotationNumberStylePreview : Control
    {
        private DrawingGroup? _drawing;

        public AnnotationNumberStylePreview()
        {
            Width = 24;
            Height = 22;
        }

        public AnnotationNumberStyle Style
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                InvalidatePreview();
            }
        }

        public Color Ink
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                InvalidatePreview();
            }
        } = Colors.DodgerBlue;

        public Color? TextColor
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                InvalidatePreview();
            }
        }

        public override void Render(DrawingContext context)
        {
            if (_drawing is null)
            {
                _drawing = new();
                using var drawing = _drawing.Open();
                DrawPreview(drawing);
            }

            using var textOptions = context.PushTextOptions(new() { TextRenderingMode = TextRenderingMode.Antialias });
            _drawing.Draw(context);
        }

        private void InvalidatePreview()
        {
            _drawing = null;
            InvalidateVisual();
        }

        private void DrawPreview(DrawingContext context)
        {
            var ink = new SolidColorBrush(Ink);
            var box = new Rect(3, 2, 18, 18);
            var foreground = new SolidColorBrush(TextColor ?? AnnotationContrast.Foreground(Ink));
            var pen = new Pen(foreground, 1.5);
            switch (Style)
            {
                case AnnotationNumberStyle.RoundedSquare:
                    context.DrawRectangle(ink, null, box, 3, 3);
                    break;
                case AnnotationNumberStyle.Plain:
                    break;
                case AnnotationNumberStyle.FilledCircle:
                case AnnotationNumberStyle.BorderedCircle:
                case AnnotationNumberStyle.OutlineCircle:
                default:
                    context.DrawEllipse(Style == AnnotationNumberStyle.OutlineCircle ? null : ink,
                        Style is AnnotationNumberStyle.OutlineCircle or AnnotationNumberStyle.BorderedCircle
                            ? pen
                            : null, box);
                    if (Style == AnnotationNumberStyle.BorderedCircle)
                        context.DrawEllipse(null, new Pen(foreground), box.Deflate(1.5));
                    break;
            }

            var text = new FormattedText("1", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                Typeface.Default, 13,
                foreground);
            var bounds = text.BuildGeometry(new())?.Bounds ?? new Rect(new(text.Width, text.Height));
            context.DrawText(text, new(12 - bounds.Center.X, 11 - bounds.Center.Y));
        }
    }
}

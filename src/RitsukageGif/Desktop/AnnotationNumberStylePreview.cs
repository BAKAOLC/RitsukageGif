using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class AnnotationNumberStylePreview : Control
    {
        public AnnotationNumberStylePreview()
        {
            Width = 24;
            Height = 22;
        }

        public AnnotationNumberStyle Style { get; set; }
        public Color Ink { get; set; } = Colors.DodgerBlue;

        public override void Render(DrawingContext context)
        {
            var ink = new SolidColorBrush(Ink);
            var box = new Rect(3, 2, 18, 18);
            var pen = new Pen(ink, 1.5);
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
                        context.DrawEllipse(null, new Pen(Brushes.White), box.Deflate(1.5));
                    break;
            }

            var text = new FormattedText("1", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                Typeface.Default, 13,
                Style is AnnotationNumberStyle.Plain or AnnotationNumberStyle.OutlineCircle ? ink : Brushes.White);
            context.DrawText(text, new(12 - text.Width / 2, 11 - text.Height / 2));
        }
    }
}

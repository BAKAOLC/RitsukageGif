using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedColorScene : Control
    {
        private const double SwatchHeight = 20;
        private readonly SolidColorBrush _swatch;
        private readonly FormattedText _original;
        private readonly FormattedText _descriptions;

        internal PinnedColorScene(PinnedColorDocument document)
        {
            var color = Color.Parse(document.Value);
            _swatch = new(color);
            var formats = new PinnedColorFormats(color);
            var description = string.Join('\n',
                formats.Format(PinnedColorFormat.Rgb, true, 4),
                formats.Format(PinnedColorFormat.Hsv, true, 4),
                formats.Format(PinnedColorFormat.Hsl, true, 4),
                formats.Format(PinnedColorFormat.Hex, true, 4));
            _original = Measure(document.PlainText, 14);
            _descriptions = Measure(description, 13);
            Extent = new(Math.Max(_original.Width, _descriptions.Width) + 12,
                SwatchHeight + _original.Height + _descriptions.Height);
        }

        internal Size Extent { get; }

        protected override Size MeasureOverride(Size availableSize)
        {
            return Extent;
        }

        public override void Render(DrawingContext context)
        {
            Draw(context);
        }

        internal void Draw(DrawingContext context)
        {
            context.FillRectangle(Brushes.White, new(Extent));
            context.FillRectangle(_swatch, new(0, 0, Extent.Width, SwatchHeight));
            context.DrawText(_original, new((Extent.Width - _original.Width) / 2, SwatchHeight));
            context.DrawText(_descriptions,
                new((Extent.Width - _descriptions.Width) / 2, SwatchHeight + _original.Height));
        }

        private static FormattedText Measure(string text, double size)
        {
            return new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                Typeface.Default, size, Brushes.Black);
        }
    }
}

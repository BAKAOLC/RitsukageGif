using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedRichTextSource(
        PinnedRichParagraph paragraph,
        Typeface defaultTypeface,
        IBrush foreground,
        double width,
        Func<PinnedRichImage, Size> imageSize,
        Action<DrawingContext, PinnedRichImage, Rect> drawImage) : ITextSource
    {
        private readonly Dictionary<PinnedRichStyle, TextRunProperties> _properties = [];

        public TextRun GetTextRun(int textSourceIndex)
        {
            if (textSourceIndex >= paragraph.Length) return new TextEndOfParagraph();
            var offset = paragraph.Start + textSourceIndex;
            var low = 0;
            var high = paragraph.Runs.Count;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (paragraph.Runs[middle].Start <= offset) low = middle + 1;
                else high = middle;
            }

            if (low == 0) return new TextEndOfParagraph();
            var run = paragraph.Runs[low - 1];
            var properties = Properties(run.Style);
            if (run.Image is { } image)
            {
                var natural = imageSize(image);
                var imageWidth = image.Width.Resolve(width, natural.Width);
                var imageHeight = image.Height.Resolve(width, natural.Height);
                if (double.IsNaN(image.Width.Value) && !double.IsNaN(image.Height.Value))
                    imageWidth = natural.Height > 0 ? natural.Width * imageHeight / natural.Height : imageHeight;
                if (double.IsNaN(image.Height.Value) && !double.IsNaN(image.Width.Value))
                    imageHeight = natural.Width > 0 ? natural.Height * imageWidth / natural.Width : imageWidth;
                return new PinnedRichImageRun(image, new(Math.Max(1, imageWidth), Math.Max(1, imageHeight)), properties,
                    drawImage);
            }

            var start = offset - run.Start;
            var remaining = run.Text.AsMemory(start);
            var length = 0;
            while (length < remaining.Length && length < 1024)
                length += StringInfo.GetNextTextElementLength(remaining.Span[length..]);
            return new TextCharacters(remaining[..length], properties);
        }

        internal TextRunProperties Properties(PinnedRichStyle style)
        {
            if (_properties.TryGetValue(style, out var properties)) return properties;
            var typeface = new Typeface(
                style.FontFamily is { Length: > 0 } family ? new(family) : defaultTypeface.FontFamily,
                style.Slant, style.Weight);
            TextDecorationCollection? decorations = null;
            if (style.Underline || style.Strike)
            {
                decorations = [];
                if (style.Underline) decorations.AddRange(TextDecorations.Underline);
                if (style.Strike) decorations.AddRange(TextDecorations.Strikethrough);
            }

            properties = new GenericTextRunProperties(typeface, style.FontSize, decorations,
                style.Foreground is { } color ? new SolidColorBrush(color) : foreground,
                style.Background is { } background ? new SolidColorBrush(background) : null, style.Baseline);
            _properties.Add(style, properties);
            return properties;
        }
    }
}

using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Media.TextFormatting.Unicode;

namespace RitsukageGif.Desktop
{
    internal sealed class ImageTextSelectionBuilder
    {
        private readonly List<ImageTextSelectionSpan> _spans = [];
        private readonly StringBuilder _text = new();
        private int _lineCount;

        internal void AppendLayout(string text, TextLayout layout, Point origin, Rect clip, int line)
        {
            var start = AppendText(text);
            AppendGlyphs(text, layout, origin, clip, start, line, 1, 1);
        }

        private int AppendText(string text)
        {
            if (_lineCount++ != 0) _text.AppendLine();
            var start = _text.Length;
            _text.Append(text);
            return start;
        }

        internal static ImageTextSelectionDocument FromOcr(OcrRecognitionResult result)
        {
            var builder = new ImageTextSelectionBuilder();
            var lineIndex = 0;
            var frame = new Rect(0, 0, result.Width, result.Height);
            foreach (var line in result.Lines)
            {
                var start = builder.AppendText(line.Text);
                var cursor = 0;
                var matched = new List<(OcrWord Word, int Offset)>();
                foreach (var word in line.Words)
                {
                    var position = line.Text.IndexOf(word.Text, cursor, StringComparison.Ordinal);
                    if (position < 0)
                    {
                        matched.Clear();
                        break;
                    }

                    matched.Add((word, position));
                    cursor = position + word.Text.Length;
                }

                if (matched.Count == 0)
                    builder.AppendOcrText(line.Text, line.Bounds, start, lineIndex, frame);
                else
                    foreach (var (word, offset) in matched)
                        builder.AppendOcrText(word.Text, word.Bounds, start + offset, lineIndex, frame);
                lineIndex++;
            }

            return builder.Build();
        }

        private void AppendOcrText(string text, OcrBounds bounds, int start, int line, Rect frame)
        {
            if (text.Length == 0 || bounds.Width <= 0 || bounds.Height <= 0) return;
            using var layout = new TextLayout(text, Typeface.Default, 32, Brushes.Black,
                flowDirection: Direction(text), textWrapping: TextWrapping.NoWrap);
            var width = layout.WidthIncludingTrailingWhitespace;
            if (width <= 0 || layout.Height <= 0) return;
            var word = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            AppendGlyphs(text, layout, word.Position, word.Intersect(frame), start, line,
                bounds.Width / width, bounds.Height / layout.Height);
        }

        private void AppendGlyphs(string text, TextLayout layout, Point origin, Rect clip,
            int start, int line, double scaleX, double scaleY)
        {
            for (var index = 0; index < text.Length;)
            {
                var length = StringInfo.GetNextTextElementLength(text.AsSpan(index));
                var glyph = layout.HitTestTextPosition(index);
                if (length > 1)
                {
                    Rect? complete = null;
                    foreach (var part in layout.HitTestTextRange(index, length))
                        complete = complete is { } previous ? previous.Union(part) : part;
                    if (complete is { } bounds) glyph = bounds;
                }

                var x = Math.Min(glyph.X, glyph.Right);
                var rectangle = new Rect(origin.X + x * scaleX, origin.Y + glyph.Y * scaleY,
                    Math.Abs(glyph.Width) * scaleX, glyph.Height * scaleY).Intersect(clip);
                if (rectangle is { Width: > 0, Height: > 0 })
                    _spans.Add(new(start + index, length, line, rectangle));
                index += length;
            }
        }

        internal static FlowDirection Direction(string text)
        {
            foreach (var rune in text.EnumerateRunes())
                switch (new Codepoint((uint)rune.Value).BiDiClass)
                {
                    case BidiClass.ArabicLetter or BidiClass.RightToLeft:
                        return FlowDirection.RightToLeft;
                    case BidiClass.LeftToRight:
                        return FlowDirection.LeftToRight;
                }

            return FlowDirection.LeftToRight;
        }

        internal ImageTextSelectionDocument Build()
        {
            return new(_text.ToString(), [.. _spans]);
        }
    }
}

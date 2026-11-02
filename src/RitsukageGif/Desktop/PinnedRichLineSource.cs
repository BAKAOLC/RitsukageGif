using Avalonia.Media.TextFormatting;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedRichLineSource(PinnedRichTextSource source, int end) : ITextSource
    {
        public TextRun GetTextRun(int textSourceIndex)
        {
            if (textSourceIndex >= end) return new TextEndOfParagraph();
            var run = source.GetTextRun(textSourceIndex);
            if (run is TextCharacters characters && characters.Text.Length > end - textSourceIndex)
                return new TextCharacters(characters.Text[..(end - textSourceIndex)], characters.Properties);
            return run;
        }
    }
}

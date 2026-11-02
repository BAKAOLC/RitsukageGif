using Avalonia;
using Avalonia.Media.TextFormatting;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedRichLine
    {
        internal required PinnedRichParagraph Paragraph { get; init; }
        internal required PinnedRichTextSource Source { get; init; }
        internal required TextParagraphProperties Properties { get; init; }
        internal required Rect Bounds { get; set; }
        internal required int Offset { get; init; }
        internal required int Length { get; init; }
        internal required double FormatWidth { get; init; }
        internal int Start => Paragraph.Start + Offset;
    }
}

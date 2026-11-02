using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedTextColorizer(IReadOnlyList<PinnedTextStyleRange> ranges)
        : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line)
        {
            var low = 0;
            var high = ranges.Count;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (ranges[middle].End <= line.Offset) low = middle + 1;
                else high = middle;
            }

            for (var index = low; index < ranges.Count && ranges[index].Start < line.EndOffset; index++)
            {
                var range = ranges[index];
                var start = Math.Max(line.Offset, range.Start);
                var end = Math.Min(line.EndOffset, range.End);
                if (start >= end) continue;
                ChangeLinePart(start, end, element =>
                {
                    var properties = element.TextRunProperties;
                    properties.SetTypeface(range.Typeface);
                    properties.SetFontRenderingEmSize(range.Size);
                    if (range.Foreground is not null) properties.SetForegroundBrush(range.Foreground);
                    if (range.Background is not null) properties.SetBackgroundBrush(range.Background);
                    if (range.Decorations is not null) properties.SetTextDecorations(range.Decorations);
                });
            }
        }
    }
}

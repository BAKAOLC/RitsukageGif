namespace RitsukageGif.Desktop
{
    internal sealed record PinnedRichTable(
        int Start,
        int Length,
        PinnedRichBlockStyle Style,
        int Rows,
        int Columns,
        double CellSpacing,
        IReadOnlyList<PinnedRichCell> Cells) : PinnedRichBlock(Start, Length, Style);
}

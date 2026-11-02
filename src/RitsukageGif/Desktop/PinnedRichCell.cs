namespace RitsukageGif.Desktop
{
    internal sealed record PinnedRichCell(
        int Row,
        int Column,
        int RowSpan,
        int ColumnSpan,
        int Start,
        int Length,
        PinnedRichBlockStyle Style,
        string VerticalAlignment,
        IReadOnlyList<PinnedRichBlock> Blocks);
}

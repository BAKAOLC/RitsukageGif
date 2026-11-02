namespace RitsukageGif.Core
{
    internal sealed record TableStructureCell(
        int Row,
        int Column,
        int RowSpan,
        int ColumnSpan,
        double X,
        double Y,
        double Width,
        double Height)
    {
        internal string Text { get; set; } = "";
    }
}

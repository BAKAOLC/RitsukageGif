namespace RitsukageGif.Desktop
{
    internal sealed record TableRecognitionGridState(
        IReadOnlyList<TableCellRange> Ranges,
        TableRecognitionAxisState Rows,
        TableRecognitionAxisState Columns,
        (int Row, int Column) Current,
        (int Row, int Column) Anchor,
        bool HasCurrent);
}

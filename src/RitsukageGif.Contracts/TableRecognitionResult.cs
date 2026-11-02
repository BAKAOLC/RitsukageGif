namespace RitsukageGif.Contracts
{
    public sealed record TableRecognitionResult(
        int Rows,
        int Columns,
        int Width,
        int Height,
        IReadOnlyList<TableCell> Cells,
        float? StructureConfidence = null);
}

namespace RitsukageGif.Desktop
{
    internal sealed record TablePrintDocument(
        double Width,
        double Height,
        double Left,
        double Top,
        double ContentWidth,
        double ContentHeight,
        double Pitch,
        double Scale,
        double[] Columns,
        int[] PanelStarts,
        int[] RowBands,
        IReadOnlyList<TablePrintCell> Cells,
        IReadOnlyList<TablePrintPage> Pages) : IDisposable
    {
        public void Dispose()
        {
            foreach (var cell in Cells)
            foreach (var line in cell.Lines)
                line.Dispose();
        }
    }
}

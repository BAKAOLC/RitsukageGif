namespace RitsukageGif.Core
{
    public readonly record struct TableCellRange(int Row, int Column, int Rows, int Columns)
    {
        public int Bottom => Row + Rows;
        public int Right => Column + Columns;
    }
}

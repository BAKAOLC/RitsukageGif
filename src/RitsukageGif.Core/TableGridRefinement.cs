namespace RitsukageGif.Core
{
    internal static class TableGridRefinement
    {
        internal static TableStructureCell[] Refine(GrayImage image, TableStructureCell[] cells)
        {
            var horizontal = Lines(image.Height, image.Width,
                (line, point) => image.Pixels[line * image.Width + point]);
            var vertical = Lines(image.Width, image.Height, (line, point) => image.Pixels[point * image.Width + line]);
            var rows = cells.Max(cell => cell.Row + cell.RowSpan);
            var columns = cells.Max(cell => cell.Column + cell.ColumnSpan);
            if (horizontal.Count != rows + 1 || vertical.Count != columns + 1) return cells;
            return
            [
                .. cells.Select(cell => new TableStructureCell(cell.Row, cell.Column, cell.RowSpan, cell.ColumnSpan,
                    vertical[cell.Column] + 2, horizontal[cell.Row] + 2,
                    Math.Max(1, vertical[cell.Column + cell.ColumnSpan] - vertical[cell.Column] - 4),
                    Math.Max(1, horizontal[cell.Row + cell.RowSpan] - horizontal[cell.Row] - 4))),
            ];
        }

        private static List<int> Lines(int count, int length, Func<int, int, byte> sample)
        {
            var lines = new List<int>();
            var start = -1;
            for (var line = 0; line <= count; line++)
            {
                var dark = 0;
                if (line < count)
                    for (var point = 0; point < length; point++)
                        if (sample(line, point) < 100)
                            dark++;
                if (dark >= length * .35)
                {
                    if (start < 0) start = line;
                }
                else if (start >= 0)
                {
                    if (line - start <= Math.Max(4, length / 100)) lines.Add((start + line - 1) / 2);
                    start = -1;
                }
            }

            return lines;
        }
    }
}

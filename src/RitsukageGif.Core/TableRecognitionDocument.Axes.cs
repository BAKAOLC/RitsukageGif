namespace RitsukageGif.Core
{
    public sealed partial class TableRecognitionDocument
    {
        public void InsertRow(int row)
        {
            InsertAxis(row, true);
        }

        public void InsertColumn(int column)
        {
            InsertAxis(column, false);
        }

        // ReSharper disable once UnusedMember.Global
        public void DeleteRow(int row)
        {
            DeleteAxis(row, true);
        }

        // ReSharper disable once UnusedMember.Global
        public void DeleteColumn(int column)
        {
            DeleteAxis(column, false);
        }

        public IReadOnlyList<int> DeleteRows(IReadOnlyList<TableCellRange> ranges)
        {
            return DeleteAxes(ranges, true);
        }

        public IReadOnlyList<int> DeleteColumns(IReadOnlyList<TableCellRange> ranges)
        {
            return DeleteAxes(ranges, false);
        }

        private void InsertAxis(int index, bool vertical)
        {
            var count = vertical ? Table.Rows : Table.Columns;
            if (index < 0 || index > count) throw new ArgumentOutOfRangeException(nameof(index));
            var occupied = new bool[vertical ? Table.Columns : Table.Rows];
            var cells = new List<TableCell>(Table.Cells.Count + occupied.Length);
            foreach (var cell in Table.Cells)
            {
                var start = vertical ? cell.Row : cell.Column;
                var span = vertical ? cell.RowSpan : cell.ColumnSpan;
                if (start >= index)
                {
                    start++;
                }
                else if (start + span > index)
                {
                    span++;
                    var otherStart = vertical ? cell.Column : cell.Row;
                    var otherSpan = vertical ? cell.ColumnSpan : cell.RowSpan;
                    Array.Fill(occupied, true, otherStart, otherSpan);
                }

                cells.Add(vertical
                    ? cell with { Row = start, RowSpan = span }
                    : cell with { Column = start, ColumnSpan = span });
            }

            for (var other = 0; other < occupied.Length; other++)
                if (!occupied[other])
                    cells.Add(vertical ? new(index, other, 1, 1, "", default) : new(other, index, 1, 1, "", default));
            Table = vertical
                ? Table with { Rows = count + 1, Cells = Ordered(cells) }
                : Table with { Columns = count + 1, Cells = Ordered(cells) };
        }

        private int[] DeleteAxes(IReadOnlyList<TableCellRange> ranges, bool vertical)
        {
            var count = vertical ? Table.Rows : Table.Columns;
            var deleted = new bool[count];
            foreach (var cell in SelectCells(ranges))
                Array.Fill(deleted, true, vertical ? cell.Row : cell.Column, vertical ? cell.RowSpan : cell.ColumnSpan);
            return DeleteIndices(deleted, vertical);
        }

        private void DeleteAxis(int index, bool vertical)
        {
            var count = vertical ? Table.Rows : Table.Columns;
            if (index < 0 || index >= count || count == 1) throw new ArgumentOutOfRangeException(nameof(index));
            var deleted = new bool[count];
            deleted[index] = true;
            DeleteIndices(deleted, vertical);
        }

        private int[] DeleteIndices(bool[] deleted, bool vertical)
        {
            var count = deleted.Length;
            // Retain the final axis when all axes are selected.
            if (deleted.All(value => value)) deleted[^1] = false;
            var before = new int[count + 1];
            for (var index = 0; index < count; index++) before[index + 1] = before[index] + (deleted[index] ? 1 : 0);
            if (before[count] == 0) return [];
            var cells = new List<TableCell>(Table.Cells.Count);
            foreach (var cell in Table.Cells)
            {
                var start = vertical ? cell.Row : cell.Column;
                var span = vertical ? cell.RowSpan : cell.ColumnSpan;
                var remaining = span - before[start + span] + before[start];
                if (remaining == 0) continue;
                var first = start;
                while (deleted[first]) first++;
                var bounds = cell.Bounds;
                bounds = vertical
                    ? bounds with
                    {
                        Y = bounds.Y + bounds.Height * (first - start) / span,
                        Height = bounds.Height * remaining / span,
                    }
                    : bounds with
                    {
                        X = bounds.X + bounds.Width * (first - start) / span,
                        Width = bounds.Width * remaining / span,
                    };
                cells.Add(vertical
                    ? cell with { Row = start - before[start], RowSpan = remaining, Bounds = bounds }
                    : cell with { Column = start - before[start], ColumnSpan = remaining, Bounds = bounds });
            }

            Table = vertical
                ? Table with { Rows = count - before[count], Cells = Ordered(cells) }
                : Table with { Columns = count - before[count], Cells = Ordered(cells) };
            return [.. Enumerable.Range(0, count).Where(index => deleted[index])];
        }
    }
}

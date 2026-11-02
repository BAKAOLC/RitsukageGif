namespace RitsukageGif.Core
{
    public sealed partial class TableRecognitionDocument(TableRecognitionResult table)
    {
        public TableRecognitionResult Table { get; private set; } = Normalize(table);

        // ReSharper disable once MemberCanBePrivate.Global
        public TableCell? CellAt(int row, int column)
        {
            return Table.Cells.FirstOrDefault(cell =>
                cell.Row <= row && row < cell.Row + cell.RowSpan &&
                cell.Column <= column && column < cell.Column + cell.ColumnSpan);
        }

        public void Restore(TableRecognitionResult table)
        {
            Table = Normalize(table);
        }

        public void SetText(int row, int column, string text)
        {
            SetCell(row, column, text, default);
        }

        public void SetValue(int row, int column, TableCellValue value)
        {
            SetCell(row, column, value.ToDisplayText(), value);
        }

        private void SetCell(int row, int column, string text, TableCellValue value)
        {
            var current = CellAt(row, column) ?? throw new ArgumentOutOfRangeException(nameof(row));
            Table = Table with
            {
                Cells =
                [
                    .. Table.Cells.Select(cell =>
                        ReferenceEquals(cell, current) ? cell with { Text = text, Value = value } : cell),
                ],
            };
        }

        public TableRecognitionResult Extract(IReadOnlyList<TableCellRange> ranges)
        {
            var selected = SelectCells(ranges);
            if (selected.Count == 0) throw new ArgumentException("Select at least one cell.", nameof(ranges));
            var bounds = Bounds(selected);
            var cells = selected
                .Select(cell => cell with { Row = cell.Row - bounds.Row, Column = cell.Column - bounds.Column })
                .ToList();
            var covered = new List<(int Left, int Right)>?[bounds.Rows];
            foreach (var cell in cells)
                for (var row = cell.Row; row < cell.Row + cell.RowSpan; row++)
                    (covered[row] ??= []).Add((cell.Column, cell.Column + cell.ColumnSpan));
            for (var row = 0; row < bounds.Rows; row++)
            {
                var column = 0;
                foreach (var interval in (covered[row] ?? []).OrderBy(interval => interval.Left))
                {
                    for (; column < interval.Left; column++) cells.Add(new(row, column, 1, 1, "", default));
                    column = interval.Right;
                }

                for (; column < bounds.Columns; column++) cells.Add(new(row, column, 1, 1, "", default));
            }

            return Table with { Rows = bounds.Rows, Columns = bounds.Columns, Cells = Ordered(cells) };
        }

        public void Clear(IReadOnlyList<TableCellRange> ranges)
        {
            var selected = SelectCells(ranges);
            Table = Table with
            {
                Cells =
                [
                    .. Table.Cells.Select(cell =>
                        selected.Contains(cell) ? cell with { Text = "", Value = default } : cell),
                ],
            };
        }

        public bool CanSplit(IReadOnlyList<TableCellRange> ranges)
        {
            return SelectCells(ranges).Any(cell => cell.RowSpan > 1 || cell.ColumnSpan > 1);
        }

        public bool CanMerge(IReadOnlyList<TableCellRange> ranges)
        {
            var selected = SelectCells(ranges);
            return CanMerge(selected, out _);
        }

        private static bool CanMerge(HashSet<TableCell> selected, out TableCellRange range)
        {
            range = selected.Count == 0 ? default : Bounds(selected);
            return selected.Count > 1 && selected.Sum(cell => (long)cell.RowSpan * cell.ColumnSpan) ==
                (long)range.Rows * range.Columns;
        }

        public void Merge(IReadOnlyList<TableCellRange> ranges)
        {
            var selected = SelectCells(ranges);
            if (!CanMerge(selected, out var range))
                throw new ArgumentException("Merged cells must form a filled rectangle.", nameof(ranges));
            var ordered = Ordered(selected);
            var left = selected.Min(cell => cell.Bounds.X);
            var top = selected.Min(cell => cell.Bounds.Y);
            var bounds = new OcrBounds(left, top, selected.Max(cell => cell.Bounds.X + cell.Bounds.Width) - left,
                selected.Max(cell => cell.Bounds.Y + cell.Bounds.Height) - top);
            var merged = new TableCell(range.Row, range.Column, range.Rows, range.Columns,
                string.Join("\n", ordered.Select(cell => cell.Text).Where(text => text.Length != 0)), bounds);
            Table = Table with { Cells = Ordered(Table.Cells.Where(cell => !selected.Contains(cell)).Append(merged)) };
        }

        public void Split(IReadOnlyList<TableCellRange> ranges)
        {
            var selected = SelectCells(ranges);
            List<TableCell> cells = [];
            foreach (var cell in Table.Cells)
            {
                if (!selected.Contains(cell) || cell is { RowSpan: 1, ColumnSpan: 1 })
                {
                    cells.Add(cell);
                    continue;
                }

                for (var row = 0; row < cell.RowSpan; row++)
                for (var column = 0; column < cell.ColumnSpan; column++)
                    cells.Add(new(cell.Row + row, cell.Column + column, 1, 1,
                            row == 0 && column == 0 ? cell.Text : "",
                            new(cell.Bounds.X + cell.Bounds.Width * column / cell.ColumnSpan,
                                cell.Bounds.Y + cell.Bounds.Height * row / cell.RowSpan,
                                cell.Bounds.Width / cell.ColumnSpan, cell.Bounds.Height / cell.RowSpan))
                        { Value = row == 0 && column == 0 ? cell.Value : default });
            }

            Table = Table with { Cells = Ordered(cells) };
        }

        private HashSet<TableCell> SelectCells(IReadOnlyList<TableCellRange> ranges)
        {
            var rows = new List<(int Left, int Right)>?[Table.Rows];
            foreach (var range in ranges)
            {
                ValidateRange(range);
                for (var row = range.Row; row < range.Bottom; row++)
                    (rows[row] ??= []).Add((range.Column, range.Right));
            }

            for (var row = 0; row < rows.Length; row++)
            {
                if (rows[row] is not { } intervals) continue;
                intervals.Sort();
                List<(int Left, int Right)> merged = [];
                foreach (var interval in intervals)
                    if (merged.Count > 0 && merged[^1].Right >= interval.Left)
                        merged[^1] = (merged[^1].Left, Math.Max(merged[^1].Right, interval.Right));
                    else merged.Add(interval);
                rows[row] = merged;
            }

            var selected = new HashSet<TableCell>(ReferenceEqualityComparer.Instance);
            foreach (var cell in Table.Cells)
                for (var row = cell.Row; row < cell.Row + cell.RowSpan; row++)
                {
                    if (rows[row] is not { } intervals) continue;
                    var low = 0;
                    var high = intervals.Count;
                    while (low < high)
                    {
                        var middle = (low + high) / 2;
                        if (intervals[middle].Right <= cell.Column) low = middle + 1;
                        else high = middle;
                    }

                    if (low == intervals.Count || intervals[low].Left >= cell.Column + cell.ColumnSpan) continue;
                    selected.Add(cell);
                    break;
                }

            return selected;
        }

        private void ValidateRange(TableCellRange range)
        {
            if (range.Row < 0 || range.Column < 0 || range.Rows < 1 || range.Columns < 1 ||
                range.Rows > Table.Rows - range.Row || range.Columns > Table.Columns - range.Column)
                throw new ArgumentOutOfRangeException(nameof(range));
        }

        private static TableCellRange Bounds(ICollection<TableCell> cells)
        {
            var top = cells.Min(cell => cell.Row);
            var left = cells.Min(cell => cell.Column);
            return new(top, left, cells.Max(cell => cell.Row + cell.RowSpan) - top,
                cells.Max(cell => cell.Column + cell.ColumnSpan) - left);
        }

        private static TableCell[] Ordered(IEnumerable<TableCell> cells)
        {
            return [.. cells.OrderBy(cell => cell.Row).ThenBy(cell => cell.Column)];
        }

        private static TableRecognitionResult Normalize(TableRecognitionResult table)
        {
            if (table.Rows < 1 || table.Columns < 1)
                throw new ArgumentException("A table must have at least one row and column.", nameof(table));
            var rows = new List<(int Left, int Right)>?[table.Rows];
            List<TableCell> missing = [];
            foreach (var cell in table.Cells)
            {
                if (cell.Text is null || cell.Row < 0 || cell.Column < 0 || cell.RowSpan < 1 || cell.ColumnSpan < 1 ||
                    cell.RowSpan > table.Rows - cell.Row || cell.ColumnSpan > table.Columns - cell.Column)
                    throw new ArgumentException("The table contains an invalid cell.", nameof(table));
                for (var row = cell.Row; row < cell.Row + cell.RowSpan; row++)
                    (rows[row] ??= []).Add((cell.Column, cell.Column + cell.ColumnSpan));
            }

            for (var row = 0; row < rows.Length; row++)
            {
                var column = 0;
                foreach (var interval in (rows[row] ?? []).OrderBy(interval => interval.Left))
                {
                    if (interval.Left < column)
                        throw new ArgumentException("Table cells must not overlap.", nameof(table));
                    for (; column < interval.Left; column++) missing.Add(new(row, column, 1, 1, "", default));
                    column = interval.Right;
                }

                for (; column < table.Columns; column++) missing.Add(new(row, column, 1, 1, "", default));
            }

            return table with
            {
                Cells = [.. table.Cells.Select(NormalizeCell), .. missing],
            };
        }

        private static TableCell NormalizeCell(TableCell cell)
        {
            if (cell.Value.Kind == TableCellValueKind.Text) return cell;
            var text = cell.Value.ToDisplayText();
            return cell.Text == text ? cell : cell with { Text = text };
        }
    }
}

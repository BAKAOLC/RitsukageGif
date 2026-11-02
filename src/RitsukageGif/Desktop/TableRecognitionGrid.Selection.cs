namespace RitsukageGif.Desktop
{
    internal sealed partial class TableRecognitionGrid
    {
        private readonly Dictionary<int, int> _columnSelectionDelta = [];
        private readonly Dictionary<int, int> _rowSelectionDelta = [];
        private TableCellRange? _activeSelection;
        private (int Row, int Column) _anchor;
        private (int Row, int Column) _current;
        private bool _hasCurrent;
        private int[] _selectedColumns = [];
        private HashSet<long>? _selectedKeys;
        private IReadOnlyList<TableCellRange>? _selectedRanges;
        private int[] _selectedRows = [];
        private TableCellRange? _simpleSelection;
        private bool _toggleSelection;

        private TableCellRange Selection { get; set; } = new(0, 0, 1, 1);

        internal TableCellRange SelectionBounds
        {
            get
            {
                var ranges = SelectedRanges;
                if (ranges.Count == 0) return Selection;
                var top = ranges.Min(range => range.Row);
                var left = ranges.Min(range => range.Column);
                return new(top, left, ranges.Max(range => range.Bottom) - top, ranges.Max(range => range.Right) - left);
            }
        }

        internal bool HasSelection =>
            _simpleSelection.HasValue || _selectedKeys is { Count: > 0 } || _activeSelection.HasValue;

        internal IReadOnlyList<TableCellRange> SelectedRanges
        {
            get
            {
                if (_selectedRanges is not null) return _selectedRanges;
                if (_simpleSelection is { } simple) return _selectedRanges = [simple];
                var keys = _selectedKeys is null ? new HashSet<long>() : new(_selectedKeys);
                if (_activeSelection is { } active) ApplyRange(keys, active, _toggleSelection);
                var ordered = keys.Order().Select(key => CellAt((int)(key >> 32), (int)key)!).ToArray();
                var ranges = new List<TableCellRange>();
                foreach (var cell in ordered)
                    if (ranges.Count > 0 && ranges[^1] is var previous && previous.Row == cell.Row &&
                        previous.Rows == cell.RowSpan && previous.Right == cell.Column)
                        ranges[^1] = previous with { Columns = previous.Columns + cell.ColumnSpan };
                    else ranges.Add(new(cell.Row, cell.Column, cell.RowSpan, cell.ColumnSpan));
                var merged = new List<TableCellRange>(ranges.Count);
                var trailing = new Dictionary<(int Column, int Columns), int>();
                foreach (var range in ranges)
                {
                    var key = (range.Column, range.Columns);
                    if (trailing.TryGetValue(key, out var index) && merged[index].Bottom == range.Row)
                    {
                        merged[index] = merged[index] with { Rows = merged[index].Rows + range.Rows };
                    }
                    else
                    {
                        trailing[key] = merged.Count;
                        merged.Add(range);
                    }
                }

                return _selectedRanges = merged;
            }
        }

        private void ClearSelection()
        {
            Selection = new(0, 0, 1, 1);
            _simpleSelection = _activeSelection = null;
            _selectedKeys = null;
            _selectedRanges = null;
            _anchor = _current = (0, 0);
            _hasCurrent = false;
            _selectedRows = new int[_rows.Count];
            _selectedColumns = new int[_columns.Count];
            _rowSelectionDelta.Clear();
            _columnSelectionDelta.Clear();
        }

        private void ClampSelection(IReadOnlyList<TableCellRange> ranges)
        {
            if (_document is not { Table: var table })
            {
                ClearSelection();
                return;
            }

            var row = Math.Clamp(_current.Row, 0, table.Rows - 1);
            var column = Math.Clamp(_current.Column, 0, table.Columns - 1);
            var kept = ranges.Select(range =>
            {
                var top = Math.Clamp(range.Row, 0, table.Rows - 1);
                var left = Math.Clamp(range.Column, 0, table.Columns - 1);
                return ExpandRange(new(top, left, Math.Min(range.Bottom, table.Rows) - top,
                    Math.Min(range.Right, table.Columns) - left));
            }).ToArray();
            _activeSelection = null;
            _selectedRanges = null;
            if (kept.Length == 1)
            {
                SetSimpleSelection(kept[0]);
            }
            else
            {
                _simpleSelection = null;
                _selectedKeys = [];
                foreach (var range in kept) ApplyRange(_selectedKeys, range, false);
                _rowSelectionDelta.Clear();
                _columnSelectionDelta.Clear();
                UpdateHeaderSelection();
                Selection = kept.Length == 0 ? new(row, column, 1, 1) : ExpandRange(new(row, column, 1, 1));
            }

            _anchor = _current = (row, column);
        }

        private static long CellKey(TableCell cell)
        {
            return ((long)cell.Row << 32) | (uint)cell.Column;
        }

        private static bool Intersects(TableCell cell, TableCellRange range)
        {
            return cell.Row < range.Bottom &&
                   cell.Row + cell.RowSpan > range.Row && cell.Column < range.Right &&
                   cell.Column + cell.ColumnSpan > range.Column;
        }

        private bool IsSelected(TableCell cell)
        {
            if (_simpleSelection is { } simple) return Intersects(cell, simple);
            var selected = _selectedKeys?.Contains(CellKey(cell)) == true;
            if (_activeSelection is { } active && Intersects(cell, active)) return !_toggleSelection || !selected;
            return selected;
        }

        private TableCellRange ExpandRange(TableCellRange range)
        {
            while (true)
            {
                var top = range.Row;
                var left = range.Column;
                var bottom = range.Bottom;
                var right = range.Right;
                for (var row = range.Row; row < range.Bottom; row++)
                {
                    var cells = _rowCells[row];
                    for (var index = FirstColumn(cells, range.Column);
                         index < cells.Length && cells[index].Column < range.Right;
                         index++)
                    {
                        var cell = cells[index];
                        top = Math.Min(top, cell.Row);
                        left = Math.Min(left, cell.Column);
                        bottom = Math.Max(bottom, cell.Row + cell.RowSpan);
                        right = Math.Max(right, cell.Column + cell.ColumnSpan);
                    }
                }

                var expanded = new TableCellRange(top, left, bottom - top, right - left);
                if (expanded == range) return range;
                range = expanded;
            }
        }

        private void SetSimpleSelection(TableCellRange range)
        {
            _hasCurrent = true;
            _simpleSelection = Selection = ExpandRange(range);
            _activeSelection = null;
            _selectedKeys = null;
            _selectedRanges = null;
            _rowSelectionDelta.Clear();
            _columnSelectionDelta.Clear();
            InvalidateTable();
        }

        private void BeginExtendedSelection(bool toggle)
        {
            _selectedKeys ??= [];
            if (_simpleSelection is { } simple) ApplyRange(_selectedKeys, simple, false);
            _simpleSelection = null;
            _activeSelection = null;
            _toggleSelection = toggle;
            _selectedRanges = null;
            _rowSelectionDelta.Clear();
            _columnSelectionDelta.Clear();
            UpdateHeaderSelection();
        }

        private void SelectTo(int row, int column, int kind)
        {
            if (_document is not { Table: var table } || CellAt(row, column) is not { } cell) return;
            _current = (cell.Row, cell.Column);
            _hasCurrent = true;
            var range = kind switch
            {
                2 => new TableCellRange(Math.Min(_anchor.Row, row), 0, Math.Abs(_anchor.Row - row) + 1, table.Columns),
                3 => new TableCellRange(0, Math.Min(_anchor.Column, column), table.Rows,
                    Math.Abs(_anchor.Column - column) + 1),
                _ => new TableCellRange(Math.Min(_anchor.Row, cell.Row), Math.Min(_anchor.Column, cell.Column),
                    Math.Max(_anchor.Row + 1, cell.Row + cell.RowSpan) - Math.Min(_anchor.Row, cell.Row),
                    Math.Max(_anchor.Column + 1, cell.Column + cell.ColumnSpan) -
                    Math.Min(_anchor.Column, cell.Column)),
            };
            range = ExpandRange(range);
            Selection = range;
            if (_extendedGesture)
            {
                _activeSelection = range;
                _selectedRanges = null;
                UpdateSelectionDelta(range);
                InvalidateTable();
            }
            else
            {
                SetSimpleSelection(range);
            }
        }

        private void CommitExtendedSelection()
        {
            if (_activeSelection is { } active)
            {
                ApplyRange(_selectedKeys!, active, _toggleSelection);
                _activeSelection = null;
                _selectedRanges = null;
                _rowSelectionDelta.Clear();
                _columnSelectionDelta.Clear();
                UpdateHeaderSelection();
            }
        }

        private void ApplyRange(HashSet<long> keys, TableCellRange range, bool toggle)
        {
            for (var row = range.Row; row < range.Bottom; row++)
            {
                var cells = _rowCells[row];
                for (var index = FirstColumn(cells, range.Column);
                     index < cells.Length && cells[index].Column < range.Right;
                     index++)
                {
                    var cell = cells[index];
                    if (cell.Row < row) continue;
                    var key = CellKey(cell);
                    if (!toggle)
                    {
                        keys.Add(key);
                        continue;
                    }

                    if (!keys.Add(key)) keys.Remove(key);
                }
            }
        }

        private void UpdateHeaderSelection()
        {
            _selectedRows = new int[_rows.Count];
            _selectedColumns = new int[_columns.Count];
            if (_selectedKeys is null) return;
            foreach (var key in _selectedKeys)
                if (CellAt((int)(key >> 32), (int)key) is { } cell)
                {
                    for (var row = cell.Row; row < cell.Row + cell.RowSpan; row++)
                        _selectedRows[row] += cell.ColumnSpan;
                    for (var column = cell.Column; column < cell.Column + cell.ColumnSpan; column++)
                        _selectedColumns[column] += cell.RowSpan;
                }
        }

        private void UpdateSelectionDelta(TableCellRange range)
        {
            _rowSelectionDelta.Clear();
            _columnSelectionDelta.Clear();
            for (var row = range.Row; row < range.Bottom; row++)
            {
                var cells = _rowCells[row];
                for (var index = FirstColumn(cells, range.Column);
                     index < cells.Length && cells[index].Column < range.Right;
                     index++)
                {
                    var cell = cells[index];
                    if (cell.Row < row) continue;
                    var selected = _selectedKeys!.Contains(CellKey(cell));
                    var change = _toggleSelection ? selected ? -1 : 1 : selected ? 0 : 1;
                    if (change == 0) continue;
                    for (var affected = cell.Row; affected < cell.Row + cell.RowSpan; affected++)
                        _rowSelectionDelta[affected] =
                            _rowSelectionDelta.GetValueOrDefault(affected) + change * cell.ColumnSpan;
                    for (var affected = cell.Column; affected < cell.Column + cell.ColumnSpan; affected++)
                        _columnSelectionDelta[affected] =
                            _columnSelectionDelta.GetValueOrDefault(affected) + change * cell.RowSpan;
                }
            }
        }

        private bool HeaderSelected(bool vertical, int index)
        {
            if (_simpleSelection is { } simple)
                return vertical
                    ? index >= simple.Row && index < simple.Bottom
                    : index >= simple.Column && index < simple.Right;
            var selected = vertical ? _selectedRows : _selectedColumns;
            var delta = vertical ? _rowSelectionDelta : _columnSelectionDelta;
            return (uint)index < (uint)selected.Length && selected[index] + delta.GetValueOrDefault(index) > 0;
        }
    }
}

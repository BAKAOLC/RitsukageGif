namespace RitsukageGif.Desktop
{
    internal sealed partial class TableRecognitionGrid
    {
        internal TableRecognitionGridState CaptureState()
        {
            return new([.. SelectedRanges],
                _rows.Capture(), _columns.Capture(), _current, _anchor, _hasCurrent);
        }

        internal void RestoreState(TableRecognitionGridState state)
        {
            if (!CommitEdit()) return;
            EndPointerAction();
            EnsureTable(true);
            _rows.Restore(state.Rows);
            _columns.Restore(state.Columns);
            _current = state.Current;
            ClampSelection(state.Ranges);
            _anchor = state.Anchor;
            _hasCurrent = state.HasCurrent;
            UpdateExtent();
            InvalidateTable();
        }

        internal void ApplyAxisChange(bool vertical, int? insertedIndex, IReadOnlyList<int>? deletedIndices)
        {
            EndPointerAction();
            var ranges = SelectedRanges;
            var current = _current;
            var anchor = _anchor;
            var axis = vertical ? _rows : _columns;
            if (insertedIndex is { } inserted) axis.Insert(inserted);
            else axis.Remove(deletedIndices!);
            EnsureTable(true);
            var mapped = new List<TableCellRange>(ranges.Count);
            foreach (var range in ranges)
            {
                var start = vertical ? range.Row : range.Column;
                var end = vertical ? range.Bottom : range.Right;
                var newStart = Map(start);
                var newEnd = Map(end);
                if (insertedIndex == end) newEnd--;
                if (newEnd <= newStart) continue;
                mapped.Add(vertical
                    ? range with { Row = newStart, Rows = newEnd - newStart }
                    : range with { Column = newStart, Columns = newEnd - newStart });
            }

            _current = MapCell(current);
            ClampSelection(mapped);
            _anchor = MapCell(anchor);
            UpdateExtent();
            InvalidateTable();

            return;

            int Map(int value)
            {
                if (insertedIndex is { } insertion) return value >= insertion ? value + 1 : value;
                var low = 0;
                var high = deletedIndices!.Count;
                while (low < high)
                {
                    var middle = low + (high - low) / 2;
                    if (deletedIndices[middle] < value) low = middle + 1;
                    else high = middle;
                }

                return value - low;
            }

            (int Row, int Column) MapCell((int Row, int Column) cell)
            {
                return vertical
                    ? (Math.Clamp(Map(cell.Row), 0, _rows.Count - 1), cell.Column)
                    : (cell.Row, Math.Clamp(Map(cell.Column), 0, _columns.Count - 1));
            }
        }
    }
}

using Avalonia;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed partial class TableRecognitionGrid
    {
        private static KeyModifiers CommandModifier =>
            Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers
            ?? (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (IsEditing || _document is not { Table: var table }) return;
            EnsureTable(true);
            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (e.Key)
            {
                case Key.F2:
                case Key.Enter when OperatingSystem.IsMacOS():
                    BeginEdit();
                    e.Handled = true;
                    return;
                case Key.A when e.KeyModifiers.HasFlag(CommandModifier):
                    SetSimpleSelection(new(0, 0, table.Rows, table.Columns));
                    e.Handled = true;
                    return;
                case Key.Tab:
                    MoveTab(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                    e.Handled = true;
                    return;
            }

            if (CellAt(_current.Row, _current.Column) is not { } current) return;
            if (e.Key == Key.Space && _hasCurrent)
            {
                e.Handled = true;
                if (current.Value.Kind == TableCellValueKind.Boolean)
                {
                    ToggleBoolean(current);
                    return;
                }

                var range = new TableCellRange(current.Row, current.Column, current.RowSpan, current.ColumnSpan);
                if (e.KeyModifiers.HasFlag(CommandModifier))
                {
                    BeginExtendedSelection(true);
                    Selection = range;
                    _activeSelection = range;
                    CommitExtendedSelection();
                    InvalidateTable();
                    return;
                }

                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    SelectTo(current.Row, current.Column, 1);
                else
                    SetSimpleSelection(range);

                return;
            }

            var row = current.Row;
            var column = current.Column;
            var control = e.KeyModifiers.HasFlag(CommandModifier);
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (e.Key)
            {
                case Key.Up: row--; break;
                case Key.Down: row += current.RowSpan; break;
                case Key.Left: column--; break;
                case Key.Right: column += current.ColumnSpan; break;
                case Key.Home:
                    column = 0;
                    if (control) row = 0;
                    break;
                case Key.End:
                    column = table.Columns - 1;
                    if (control) row = table.Rows - 1;
                    break;
                case Key.PageUp:
                    row = _rows.Find(Math.BitDecrement(_rows.Position(row + current.RowSpan)) -
                                     _scroll.Viewport.Height); break;
                case Key.PageDown: row = _rows.Find(_rows.Position(row) + _scroll.Viewport.Height); break;
                default: return;
            }

            row = Math.Clamp(row, 0, table.Rows - 1);
            column = Math.Clamp(column, 0, table.Columns - 1);
            if (CellAt(row, column) is not { } next) return;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                _extendedGesture = control;
                if (control) BeginExtendedSelection(false);
                SelectTo(next.Row, next.Column, 1);
                CommitExtendedSelection();
                _extendedGesture = false;
            }
            else
            {
                _anchor = _current = (next.Row, next.Column);
                _hasCurrent = true;
                if (!control) SetSimpleSelection(new(next.Row, next.Column, next.RowSpan, next.ColumnSpan));
                else InvalidateTable();
            }

            EnsureVisible(next);
            e.Handled = true;
        }

        private void MoveTab(int direction)
        {
            if (_document is not { Table: var table } ||
                CellAt(_current.Row, _current.Column) is not { } current) return;
            var row = current.Row;
            var column = direction > 0 ? current.Column + current.ColumnSpan : current.Column - 1;
            var wrapped = false;
            while (true)
            {
                if (column >= table.Columns)
                {
                    row++;
                    column = 0;
                }
                else if (column < 0)
                {
                    row--;
                    column = table.Columns - 1;
                }

                if (row < 0)
                {
                    row = table.Rows - 1;
                    wrapped = true;
                }
                else if (row >= table.Rows)
                {
                    row = 0;
                    wrapped = true;
                }

                if (wrapped && (direction > 0
                        ? row > current.Row || (row == current.Row && column >= current.Column)
                        : row < current.Row || (row == current.Row && column <= current.Column))) return;
                var next = CellAt(row, column);
                if (next is not null && !ReferenceEquals(next, current))
                {
                    _anchor = _current = (next.Row, next.Column);
                    SetSimpleSelection(new(next.Row, next.Column, next.RowSpan, next.ColumnSpan));
                    EnsureVisible(next);
                    return;
                }

                column = direction > 0 ? next?.Column + next?.ColumnSpan ?? column + 1 : (next?.Column ?? column) - 1;
            }
        }
    }
}

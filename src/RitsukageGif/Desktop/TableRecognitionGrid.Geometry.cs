using System.Globalization;
using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class TableRecognitionGrid
    {
        private Rect Viewport =>
            new(_scroll.Offset.X, _scroll.Offset.Y, _scroll.Viewport.Width, _scroll.Viewport.Height);

        private void EnsureTable(bool preserve)
        {
            var typeface = new Typeface(TextElement.GetFontFamily(this), TextElement.GetFontStyle(this),
                TextElement.GetFontWeight(this));
            var fontSize = TextElement.GetFontSize(this);
            var foreground = TextElement.GetForeground(this) ?? AppTheme.Ink;
            var styleChanged = _typeface != typeface || !_fontSize.Equals(fontSize) ||
                               !ReferenceEquals(_foreground, foreground);
            _typeface = typeface;
            _fontSize = fontSize;
            _foreground = foreground;
            var table = _document?.Table;
            if (ReferenceEquals(table, _indexedTable) && !styleChanged) return;
            if (styleChanged)
            {
                _editor.FontFamily = typeface.FontFamily;
                _editor.FontSize = fontSize;
                _editor.FontStyle = typeface.Style;
                _editor.FontWeight = typeface.Weight;
                _editor.Foreground = foreground;
                _booleanEditor.FontFamily = typeface.FontFamily;
                _booleanEditor.FontSize = fontSize;
                _booleanEditor.FontStyle = typeface.Style;
                _booleanEditor.FontWeight = typeface.Weight;
                _booleanEditor.Foreground = foreground;
                _textCache.Clear();
                _previousTextCache.Clear();
                _headerTextCache.Clear();
                _previousHeaderTextCache.Clear();
            }

            if (!ReferenceEquals(table, _indexedTable))
            {
                _indexedTable = table;
                if (table is null)
                {
                    _rowCells = [];
                }
                else
                {
                    var lists = new List<TableCell>[table.Rows];
                    for (var row = 0; row < lists.Length; row++) lists[row] = [];
                    foreach (var cell in table.Cells)
                        for (var row = cell.Row; row < cell.Row + cell.RowSpan; row++)
                            lists[row].Add(cell);
                    _rowCells = [.. lists.Select(cells => cells.OrderBy(cell => cell.Column).ToArray())];
                }
            }

            _rows.Reset(table?.Rows ?? 0, Math.Max(24, fontSize + 12), preserve);
            _columns.Reset(table?.Columns ?? 0, Math.Max(100, fontSize * 10 + 10), preserve);
            _headerHeight = Math.Max(24, fontSize + 10);
            _headerWidth = Math.Max(42,
                NewText((table?.Rows ?? 1).ToString(CultureInfo.InvariantCulture)).WidthIncludingTrailingWhitespace +
                14);
            UpdateExtent();
        }

        private void UpdateExtent()
        {
            ColumnDefinitions[0].Width = new(_headerWidth);
            RowDefinitions[0].Height = new(_headerHeight);
            _body.Width = _canvas.Width = _columns.Total;
            _body.Height = _canvas.Height = _rows.Total;
            InvalidateMeasure();
            InvalidateArrange();
        }

        private TableCell? CellAt(int row, int column)
        {
            if ((uint)row >= (uint)_rowCells.Length) return null;
            var cells = _rowCells[row];
            var index = FirstColumn(cells, column);
            return index < cells.Length && cells[index].Column <= column ? cells[index] : null;
        }

        private static int FirstColumn(TableCell[] cells, int column)
        {
            var first = 0;
            var last = cells.Length;
            while (first < last)
            {
                var middle = first + (last - first) / 2;
                if (cells[middle].Column + cells[middle].ColumnSpan <= column) first = middle + 1;
                else last = middle;
            }

            return first;
        }

        private Rect CellBounds(TableCell cell)
        {
            return new(_columns.Position(cell.Column),
                _rows.Position(cell.Row),
                _columns.Position(cell.Column + cell.ColumnSpan) - _columns.Position(cell.Column),
                _rows.Position(cell.Row + cell.RowSpan) - _rows.Position(cell.Row));
        }

        private void EnsureVisible(TableCell cell)
        {
            var bounds = CellBounds(cell);
            var viewport = Viewport;
            var x = _scroll.Offset.X;
            var y = _scroll.Offset.Y;
            if (bounds.Left < viewport.Left) x = bounds.Left;
            else if (bounds.Right > viewport.Right) x = bounds.Right - _scroll.Viewport.Width;
            if (bounds.Top < viewport.Top) y = bounds.Top;
            else if (bounds.Bottom > viewport.Bottom) y = bounds.Bottom - _scroll.Viewport.Height;
            _scroll.Offset = new(Math.Max(0, x), Math.Max(0, y));
        }
    }
}

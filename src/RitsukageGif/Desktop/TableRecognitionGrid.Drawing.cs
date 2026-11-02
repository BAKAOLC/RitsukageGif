using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class TableRecognitionGrid
    {
        private readonly HashSet<TableCell> _drawnCells = new(ReferenceEqualityComparer.Instance);
        private Dictionary<(bool Vertical, int Index, double Width), FormattedText> _headerTextCache = [];
        private Dictionary<(bool Vertical, int Index, double Width), FormattedText> _previousHeaderTextCache = [];
        private Dictionary<(string Text, double Width, double Height), FormattedText> _previousTextCache = [];
        private Dictionary<(string Text, double Width, double Height), FormattedText> _textCache = [];

        internal void DrawTable(DrawingContext context)
        {
            EnsureTable(true);
            var viewport = Viewport;
            context.DrawRectangle(AppTheme.Surface, null, viewport);
            _drawnCells.Clear();
            (_textCache, _previousTextCache) = (_previousTextCache, _textCache);
            _textCache.Clear();
            (_headerTextCache, _previousHeaderTextCache) = (_previousHeaderTextCache, _headerTextCache);
            _headerTextCache.Clear();
            if (_document is null || viewport.Width <= 0 || viewport.Height <= 0) return;
            var firstRow = _rows.Find(viewport.Top);
            var lastRow = _rows.Find(viewport.Bottom);
            var firstColumn = _columns.Find(viewport.Left);
            var lastColumn = _columns.Find(viewport.Right);
            var border = new Pen(AppTheme.Outline);
            var selectedBorder = new Pen(AppTheme.Accent);
            using var visible = context.PushClip(viewport);
            for (var row = firstRow; row <= lastRow; row++)
            {
                var cells = _rowCells[row];
                for (var index = FirstColumn(cells, firstColumn);
                     index < cells.Length && cells[index].Column <= lastColumn;
                     index++)
                {
                    var cell = cells[index];
                    if (!_drawnCells.Add(cell)) continue;
                    var bounds = CellBounds(cell);
                    if (!bounds.Intersects(viewport)) continue;
                    var selected = IsSelected(cell);
                    context.DrawRectangle(selected ? AppTheme.SurfaceTint : AppTheme.Surface,
                        selected ? selectedBorder : border, bounds);
                    using (context.PushClip(bounds.Deflate(4)))
                    {
                        var textBounds = bounds;
                        if (cell.Value.Kind == TableCellValueKind.Boolean)
                        {
                            DrawBoolean(context, cell);
                            var inset = CheckBounds(cell).Width + 5;
                            textBounds = new(bounds.X + inset, bounds.Y, Math.Max(0, bounds.Width - inset),
                                bounds.Height);
                        }

                        var text = CellText(cell.Text, textBounds);
                        context.DrawText(text,
                            new(textBounds.X + 5, bounds.Y + Math.Max(4, (bounds.Height - text.Height) / 2)));
                    }

                    if (IsKeyboardFocusWithin && _hasCurrent && cell.Row == _current.Row &&
                        cell.Column == _current.Column)
                        context.DrawRectangle(null, new Pen(AppTheme.Accent, 1, DashStyle.Dot), bounds.Deflate(2));
                }
            }
        }

        internal void DrawHeader(DrawingContext context, int kind, Size size)
        {
            context.DrawRectangle(AppTheme.Surface, new Pen(AppTheme.Outline), new(size));
            if (_document is null || kind == 0) return;
            var vertical = kind == 2;
            var axis = vertical ? _rows : _columns;
            var offset = vertical ? _scroll.Offset.Y : _scroll.Offset.X;
            var extent = vertical
                ? Math.Min(size.Height, _scroll.Viewport.Height)
                : Math.Min(size.Width, _scroll.Viewport.Width);
            if (extent <= 0) return;
            using var clip =
                context.PushClip(vertical ? new(0, 0, size.Width, extent) : new Rect(0, 0, extent, size.Height));
            var first = axis.Find(offset);
            var last = axis.Find(offset + extent);
            for (var index = first; index <= last; index++)
            {
                var bounds = vertical
                    ? new Rect(0, axis.Position(index) - offset, size.Width, axis.Extent(index))
                    : new Rect(axis.Position(index) - offset, 0, axis.Extent(index), size.Height);
                var current = _hasCurrent && index == (vertical ? _current.Row : _current.Column);
                context.DrawRectangle(
                    HeaderSelected(vertical, index) || current ? AppTheme.SurfaceTint : AppTheme.Surface,
                    new Pen(AppTheme.Outline), bounds);
                var key = (vertical, index, bounds.Width);
                if (!_headerTextCache.TryGetValue(key, out var text))
                {
                    if (!_previousHeaderTextCache.TryGetValue(key, out text))
                    {
                        text = NewText((index + 1).ToString(CultureInfo.InvariantCulture));
                        text.MaxTextWidth = Math.Max(1, bounds.Width - 8);
                        text.Trimming = TextTrimming.CharacterEllipsis;
                    }

                    _headerTextCache.Add(key, text);
                }

                using var cellClip = context.PushClip(bounds.Deflate(2));
                context.DrawText(text,
                    new(bounds.X + Math.Max(4, (bounds.Width - text.WidthIncludingTrailingWhitespace) / 2),
                        bounds.Y + Math.Max(2, (bounds.Height - text.Height) / 2)));
            }
        }

        private FormattedText CellText(string value, Rect bounds)
        {
            var key = (value, Math.Max(1, bounds.Width - 10), Math.Max(_fontSize * 2, bounds.Height - 8));
            if (_textCache.TryGetValue(key, out var text)) return text;
            if (!_previousTextCache.TryGetValue(key, out text))
            {
                text = NewText(value);
                text.MaxTextWidth = key.Item2;
                text.MaxTextHeight = key.Item3;
                text.Trimming = TextTrimming.CharacterEllipsis;
            }

            _textCache.Add(key, text);
            return text;
        }

        private FormattedText NewText(string value)
        {
            return new(value, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, _typeface, _fontSize, _foreground);
        }
    }
}

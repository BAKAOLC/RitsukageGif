using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed partial class TableRecognitionGrid
    {
        private DispatcherTimer? _autoScroll;
        private IPointer? _capturedPointer;
        private int _dragKind;
        private Point _dragViewportPoint;
        private bool _extendedGesture;
        private Point _pressPosition;
        private bool _pressedSelectedCell;
        private double _resizeExtent;
        private int _resizeIndex = -1;
        private double _resizePosition;
        private bool _resizeRows;

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            EnsureTable(true);
            if (_document is not { Table: var table }) return;
            if (IsEditing && Inside(ActiveEditor, e.Source)) return;
            if (IsEditing && !CommitEdit())
            {
                e.Handled = true;
                return;
            }

            var properties = e.GetCurrentPoint(this).Properties;
            var vertical = Inside(_rowHeader, e.Source);
            var horizontal = Inside(_columnHeader, e.Source);
            var corner = Inside(_corner, e.Source);
            if (properties is { IsLeftButtonPressed: false, IsRightButtonPressed: false }) return;
            var point = e.GetPosition(_body);
            if (properties.IsRightButtonPressed)
            {
                if (vertical || horizontal)
                {
                    var headerPosition = vertical
                        ? e.GetPosition(_rowHeader).Y + _scroll.Offset.Y
                        : e.GetPosition(_columnHeader).X + _scroll.Offset.X;
                    var headerAxis = vertical ? _rows : _columns;
                    if (headerPosition >= 0 && headerPosition < headerAxis.Total)
                    {
                        var index = headerAxis.Find(headerPosition);
                        if (!HeaderSelected(vertical, index))
                        {
                            _anchor = vertical ? (index, 0) : (0, index);
                            _extendedGesture = false;
                            SelectTo(vertical ? index : 0, vertical ? 0 : index, vertical ? 2 : 3);
                        }
                    }
                }
                else if (!corner && HitCell(point) is { } right)
                {
                    Focus();
                    if (!IsSelected(right))
                        SetSimpleSelection(new(right.Row, right.Column, right.RowSpan, right.ColumnSpan));
                    _current = (right.Row, right.Column);
                }

                return;
            }

            Focus();
            if (corner)
            {
                _anchor = _current = (0, 0);
                SetSimpleSelection(new(0, 0, table.Rows, table.Columns));
                e.Handled = true;
                return;
            }

            var position = vertical ? e.GetPosition(_rowHeader).Y + _scroll.Offset.Y
                : horizontal ? e.GetPosition(_columnHeader).X + _scroll.Offset.X : 0;
            var axis = vertical ? _rows : _columns;
            var boundary = vertical || horizontal ? axis.Boundary(position, 4) : -1;
            if (boundary >= 0)
            {
                if (e.ClickCount >= 2)
                {
                    AutoFit(vertical, boundary);
                }
                else
                {
                    _resizeRows = vertical;
                    _resizeIndex = boundary;
                    _resizeExtent = axis.Extent(boundary);
                    var press = e.GetPosition(this);
                    _resizePosition = vertical ? press.Y : press.X;
                    CapturePointer(e.Pointer);
                    Cursor = new(vertical ? StandardCursorType.SizeNorthSouth : StandardCursorType.SizeWestEast);
                }

                e.Handled = true;
                return;
            }

            var cell = vertical ? CellAt(_rows.Find(position), 0)
                : horizontal ? CellAt(0, _columns.Find(position)) : HitCell(point);
            if (cell is null || ((vertical || horizontal) && (position < 0 || position >= axis.Total))) return;
            if (!vertical && !horizontal && cell.Value.Kind == TableCellValueKind.Boolean &&
                CheckBounds(cell).Contains(point))
            {
                _pressedBoolean = e.ClickCount % 2 == 1 ? cell : null;
                CapturePointer(e.Pointer);
                e.Handled = true;
                return;
            }

            var shift = !vertical && !horizontal && e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            var control = !vertical && !horizontal && e.KeyModifiers.HasFlag(CommandModifier);
            _pressedSelectedCell = !vertical && !horizontal && !shift && !control && IsSelected(cell);
            _pressPosition = point;
            _dragKind = vertical ? 2 : horizontal ? 3 : 1;
            _extendedGesture = control;
            if (control) BeginExtendedSelection(!shift);
            if (!shift)
                _anchor = vertical ? (_rows.Find(position), 0)
                    : horizontal ? (0, _columns.Find(position)) : (cell.Row, cell.Column);
            SelectTo(vertical ? _rows.Find(position) : cell.Row,
                horizontal ? _columns.Find(position) : cell.Column, _dragKind);
            if (e.ClickCount >= 2 && !vertical && !horizontal)
            {
                CommitExtendedSelection();
                _dragKind = 0;
                BeginEdit();
            }
            else
            {
                CapturePointer(e.Pointer);
                _dragViewportPoint = point - _scroll.Offset;
            }

            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var point = e.GetPosition(_body);
            if (_resizeIndex >= 0)
            {
                var axis = _resizeRows ? _rows : _columns;
                var position = e.GetPosition(this);
                axis.Resize(_resizeIndex, Math.Max(_fontSize + 8,
                    _resizeExtent + (_resizeRows ? position.Y : position.X) - _resizePosition));
                UpdateExtent();
                InvalidateTable();
                e.Handled = true;
                return;
            }

            if (_dragKind == 0)
            {
                var vertical = Inside(_rowHeader, e.Source);
                var horizontal = Inside(_columnHeader, e.Source);
                var position = vertical ? e.GetPosition(_rowHeader).Y + _scroll.Offset.Y
                    : horizontal ? e.GetPosition(_columnHeader).X + _scroll.Offset.X : 0;
                Cursor = vertical && _rows.Boundary(position, 4) >= 0 ? new(StandardCursorType.SizeNorthSouth)
                    : horizontal && _columns.Boundary(position, 4) >= 0 ? new(StandardCursorType.SizeWestEast) : null;
                return;
            }

            var tap = Application.Current?.PlatformSettings?.GetTapSize(PointerType.Mouse) ?? new Size(4, 4);
            if (Math.Abs(point.X - _pressPosition.X) > tap.Width || Math.Abs(point.Y - _pressPosition.Y) > tap.Height)
                _pressedSelectedCell = false;
            _dragViewportPoint = point - _scroll.Offset;
            UpdateDragSelection();
            UpdateAutoScroll();
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_capturedPointer is null) return;
            var boolean = _pressedBoolean;
            var toggle = boolean is not null && CheckBounds(boolean).Contains(e.GetPosition(_body)) &&
                         e.InitialPressMouseButton == MouseButton.Left;
            var edit = _dragKind == 1 && _pressedSelectedCell;
            EndPointerAction();
            if (toggle) ToggleBoolean(boolean!);
            else if (edit) BeginEdit();
            e.Handled = true;
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            EndPointerAction();
        }

        private void CapturePointer(IPointer pointer)
        {
            _capturedPointer = pointer;
            pointer.Capture(this);
        }

        private void EndPointerAction()
        {
            CommitExtendedSelection();
            _autoScroll?.Stop();
            _dragKind = 0;
            _resizeIndex = -1;
            _extendedGesture = false;
            _pressedSelectedCell = false;
            _pressedBoolean = null;
            var pointer = _capturedPointer;
            _capturedPointer = null;
            if (pointer is not null && ReferenceEquals(pointer.Captured, this)) pointer.Capture(null);
            Cursor = null;
            InvalidateTable();
        }

        private TableCell? HitCell(Point point)
        {
            return point.X < 0 || point.Y < 0 || point.X >= _columns.Total || point.Y >= _rows.Total
                ? null
                : CellAt(_rows.Find(point.Y), _columns.Find(point.X));
        }

        private static bool Inside(Visual control, object? source)
        {
            return source is Visual visual &&
                   (ReferenceEquals(control, visual) || control.IsVisualAncestorOf(visual));
        }

        private void UpdateDragSelection()
        {
            if (_document is null || _dragKind == 0) return;
            var point = _dragViewportPoint + _scroll.Offset;
            SelectTo(_rows.Find(point.Y), _columns.Find(point.X), _dragKind);
        }

        private void UpdateAutoScroll()
        {
            var width = _scroll.Viewport.Width;
            var height = _scroll.Viewport.Height;
            var active = (_dragKind != 2 && (_dragViewportPoint.X < 16 || _dragViewportPoint.X > width - 16)) ||
                         (_dragKind != 3 && (_dragViewportPoint.Y < 16 || _dragViewportPoint.Y > height - 16));
            if (!active)
            {
                _autoScroll?.Stop();
                return;
            }

            _autoScroll ??= new(TimeSpan.FromMilliseconds(33), DispatcherPriority.Input, (_, _) =>
            {
                if (_dragKind == 0)
                {
                    _autoScroll?.Stop();
                    return;
                }

                var x = _dragKind == 2 ? 0 : ScrollDelta(_dragViewportPoint.X, _scroll.Viewport.Width);
                var y = _dragKind == 3 ? 0 : ScrollDelta(_dragViewportPoint.Y, _scroll.Viewport.Height);
                _scroll.Offset = new(Math.Max(0, _scroll.Offset.X + x), Math.Max(0, _scroll.Offset.Y + y));
                UpdateDragSelection();
            });
            _autoScroll.Start();
        }

        private static double ScrollDelta(double position, double extent)
        {
            return position < 16
                ? -Math.Clamp((16 - position) / 2, 1, 48)
                : position > extent - 16
                    ? Math.Clamp((position - extent + 16) / 2, 1, 48)
                    : 0;
        }

        private void AutoFit(bool vertical, int index)
        {
            var size = vertical
                ? _fontSize + 12
                : NewText((index + 1).ToString()).WidthIncludingTrailingWhitespace + 12;
            if (vertical)
            {
                foreach (var cell in _rowCells[index])
                {
                    var text = NewText(cell.Text);
                    var checkWidth = cell.Value.Kind == TableCellValueKind.Boolean ? CheckBounds(cell).Width + 5 : 0;
                    text.MaxTextWidth = Math.Max(1, CellBounds(cell).Width - 10 - checkWidth);
                    var other = _rows.Position(cell.Row + cell.RowSpan) - _rows.Position(cell.Row) -
                                _rows.Extent(index);
                    size = Math.Max(size, text.Height + 10 - other);
                }
            }
            else
            {
                var first = _rows.Find(_scroll.Offset.Y);
                var last = _rows.Find(_scroll.Offset.Y + _scroll.Viewport.Height);
                for (var row = first; row <= last; row++)
                    if (CellAt(row, index) is { } cell)
                    {
                        var other = _columns.Position(cell.Column + cell.ColumnSpan) - _columns.Position(cell.Column) -
                                    _columns.Extent(index);
                        var checkWidth = cell.Value.Kind == TableCellValueKind.Boolean
                            ? CheckBounds(cell).Width + 5
                            : 0;
                        size = Math.Max(size,
                            NewText(cell.Text).WidthIncludingTrailingWhitespace + 10 + checkWidth - other);
                    }
            }

            (vertical ? _rows : _columns).Resize(index, Math.Max(_fontSize + 8, size));
            UpdateExtent();
            InvalidateTable();
        }
    }
}

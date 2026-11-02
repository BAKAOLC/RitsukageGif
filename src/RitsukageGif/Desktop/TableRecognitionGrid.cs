using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class TableRecognitionGrid : Grid
    {
        private readonly Canvas _body;
        private readonly TableRecognitionCanvas _canvas;
        private readonly TableRecognitionHeaders _columnHeader;
        private readonly TableRecognitionAxis _columns = new();
        private readonly TableRecognitionHeaders _corner;

        private readonly TextBox _editor = new()
        {
            IsVisible = false, MinWidth = 0, MinHeight = 0, Padding = new(5, 3),
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        private readonly TableRecognitionHeaders _rowHeader;
        private readonly TableRecognitionAxis _rows = new();
        private readonly ScrollViewer _scroll;
        private TableRecognitionDocument? _document;
        private TableCell? _editing;
        private double _fontSize = 13;
        private IBrush _foreground = AppTheme.Ink;
        private double _headerHeight = 26;
        private double _headerWidth = 42;
        private TableRecognitionResult? _indexedTable;
        private TableCell[][] _rowCells = [];
        private Typeface _typeface = new(FontFamily.Default);

        internal TableRecognitionGrid()
        {
            Focusable = true;
            ColumnDefinitions = new("42,*");
            RowDefinitions = new("26,*");
            _canvas = new(this);
            _body = new()
            {
                ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Children = { _canvas, _editor, _booleanEditor, _invalidEditBorder },
            };
            _scroll = new()
            {
                BringIntoViewOnFocusChange = false,
                Content = _body, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Top,
            };
            _scroll.ScrollChanged += OnScrollChanged;
            SetRow(_scroll, 1);
            SetColumn(_scroll, 1);
            Children.Add(_scroll);
            _columnHeader = new(this, 1) { ClipToBounds = true };
            SetColumn(_columnHeader, 1);
            Children.Add(_columnHeader);
            _rowHeader = new(this, 2) { ClipToBounds = true };
            SetRow(_rowHeader, 1);
            Children.Add(_rowHeader);
            _corner = new(this, 0) { ClipToBounds = true };
            Children.Add(_corner);
            _editor.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
            _booleanEditor.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
            _editor.LostFocus += (_, _) => CommitEdit();
            _booleanEditor.LostFocus += (_, _) =>
            {
                if (!_booleanEditor.IsDropDownOpen) CommitEdit();
            };
            ActualThemeVariantChanged += (_, _) =>
            {
                _textCache.Clear();
                _previousTextCache.Clear();
                _headerTextCache.Clear();
                _previousHeaderTextCache.Clear();
                InvalidateTable();
            };
        }

        internal TableRecognitionDocument? Document
        {
            get => _document;
            set
            {
                if (!CommitEdit()) return;
                EndPointerAction();
                _document = value;
                _indexedTable = null;
                ClearSelection();
                EnsureTable(false);
                InvalidateMeasure();
                InvalidateTable();
            }
        }

        internal bool IsEditing => _editing is not null;

        internal event Action? Changing;

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            EndPointerAction();
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == TextElement.FontFamilyProperty || change.Property == TextElement.FontSizeProperty ||
                change.Property == TextElement.FontWeightProperty || change.Property == TextElement.FontStyleProperty ||
                change.Property == TextElement.ForegroundProperty)
            {
                InvalidateMeasure();
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (_canvas is not null) InvalidateTable();
            }
        }

        private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            InvalidateArrange();
            InvalidateTable();
        }

        internal void Refresh()
        {
            if (!CommitEdit()) return;
            var ranges = SelectedRanges.ToArray();
            EnsureTable(true);
            ClampSelection(ranges);
            InvalidateMeasure();
            InvalidateTable();
        }

        internal bool CommitEdit(bool save = true)
        {
            if (_editing is not { } cell || _document is not { } document) return true;
            var value = cell.Value;
            if (save && !TryEditorValue(cell, out value)) return false;
            _editing = null;
            if (save)
                if (cell.Value.Kind == TableCellValueKind.Text
                        ? (_editor.Text ?? "") != cell.Text
                        : value != cell.Value)
                {
                    Changing?.Invoke();
                    if (cell.Value.Kind == TableCellValueKind.Text)
                        document.SetText(cell.Row, cell.Column, _editor.Text ?? "");
                    else document.SetValue(cell.Row, cell.Column, value);
                    EnsureTable(true);
                }

            _editor.IsVisible = _booleanEditor.IsVisible = false;
            _editor.Text = null;
            _invalidEditBorder.IsVisible = false;
            ToolTip.SetTip(_editor, null);
            InvalidateTable();
            return true;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            EnsureTable(true);
            return base.MeasureOverride(availableSize);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (_editing is { } cell)
            {
                var bounds = CellBounds(cell);
                Canvas.SetLeft(ActiveEditor, bounds.X);
                Canvas.SetTop(ActiveEditor, bounds.Y);
                ActiveEditor.Width = bounds.Width;
                ActiveEditor.Height = bounds.Height;
                Canvas.SetLeft(_invalidEditBorder, bounds.X);
                Canvas.SetTop(_invalidEditBorder, bounds.Y);
                _invalidEditBorder.Width = bounds.Width;
                _invalidEditBorder.Height = bounds.Height;
            }

            return base.ArrangeOverride(finalSize);
        }

        private void BeginEdit()
        {
            if (CellAt(_current.Row, _current.Column) is not { } cell) return;
            _editing = cell;
            _editor.Text = cell.Text;
            _booleanEditor.SelectedIndex = cell.Value.Boolean ? 1 : 0;
            _editor.IsVisible = cell.Value.Kind != TableCellValueKind.Boolean;
            _booleanEditor.IsVisible = cell.Value.Kind == TableCellValueKind.Boolean;
            EnsureVisible(cell);
            InvalidateArrange();
            ActiveEditor.Focus();
            _editor.SelectAll();
        }

        private void InvalidateTable()
        {
            _canvas.InvalidateVisual();
            _rowHeader.InvalidateVisual();
            _columnHeader.InvalidateVisual();
            _corner.InvalidateVisual();
        }
    }
}

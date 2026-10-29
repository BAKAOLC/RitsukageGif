using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ToolbarToolsWindow : Window
    {
        private readonly List<Border> _columnCards = [];
        private readonly ScrollViewer _columnScroll;
        private readonly StackPanel _columns = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        private readonly Border _newColumn;

        private readonly WrapPanel _pool = new()
            { Orientation = Orientation.Horizontal, ItemWidth = 40, ItemHeight = 36 };

        private readonly Border _poolArea;
        private readonly List<List<ToolbarDragHandle>> _toolCells = [];
        private ToolbarPreferences _draft;
        private int? _draggedColumn;
        private ScreenshotTool? _draggedTool;

        public ToolbarToolsWindow(ToolbarPreferences preferences)
        {
            _draft = preferences.Normalize();
            Title = UiText.Get("ToolbarToolsTitle");
            Width = 624;
            MinWidth = 400;
            MinHeight = 280;
            MaxHeight = 520;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Workspace;
            Foreground = AppTheme.Ink;
            var layout = new Grid { RowDefinitions = new("Auto,Auto,Auto,Auto"), Margin = new(12), RowSpacing = 10 };
            layout.Children.Add(new TextBlock
            {
                Text = UiText.Get("ToolbarToolsDragHint"), TextWrapping = TextWrapping.Wrap, FontSize = 12,
                Foreground = AppTheme.Muted,
            });
            _newColumn = new()
            {
                Width = 64, MinHeight = 112, BorderBrush = AppTheme.Outline, BorderThickness = new(1),
                Background = AppTheme.SurfaceTint, CornerRadius = new(4), Padding = new(4),
                Child = new TextBlock
                {
                    Text = UiText.Get("ToolbarNewColumn"), TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center, FontSize = 12,
                },
            };
            ToolTip.SetTip(_newColumn, UiText.Get("ToolbarNewColumnHint"));
            _columnScroll = new()
            {
                Content = _columns, MaxHeight = 252, MinHeight = 128,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            Grid.SetRow(_columnScroll, 1);
            layout.Children.Add(_columnScroll);
            var poolContent = new StackPanel { Spacing = 6 };
            poolContent.Children.Add(new TextBlock { Text = UiText.Get("ToolbarToolsOutside"), FontSize = 12 });
            poolContent.Children.Add(_pool);
            _poolArea = new()
            {
                Child = poolContent, Padding = new(8), MinHeight = 74, Background = AppTheme.SurfaceTint,
                BorderBrush = AppTheme.Outline, BorderThickness = new(1), CornerRadius = new(4),
            };
            Grid.SetRow(_poolArea, 2);
            layout.Children.Add(_poolArea);
            var footer = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto"), ColumnSpacing = 8 };
            footer.Children.Add(ToolbarDragHandle.Command("ToolbarRestoreDefaults", () =>
            {
                _draft = _draft with { Columns = new ToolbarPreferences().Columns };
                RenderColumns();
            }));
            var cancel = ToolbarDragHandle.Command("Cancel", () => Close(null));
            Grid.SetColumn(cancel, 2);
            footer.Children.Add(cancel);
            var save = ToolbarDragHandle.Command("ImageSave", () => Close(_draft.Normalize()));
            save.Classes.Add("primary");
            Grid.SetColumn(save, 3);
            footer.Children.Add(save);
            Grid.SetRow(footer, 3);
            layout.Children.Add(footer);
            Content = layout;
            AddHandler(KeyDownEvent, (_, args) =>
            {
                if (args.Handled || args.Key != Key.Escape) return;
                Close(null);
                args.Handled = true;
            }, RoutingStrategies.Bubble);
            Opened += (_, _) =>
            {
                if (Screens.ScreenFromWindow(this) is not { } screen) return;
                MaxWidth = Math.Max(400, screen.WorkingArea.Width / RenderScaling - 48);
                Width = Math.Min(Width, MaxWidth);
                MaxHeight = Math.Max(280, screen.WorkingArea.Height / RenderScaling - 48);
            };
            RenderColumns();
        }

        private void BeginTool(ScreenshotTool tool, Point point)
        {
            _draggedTool = tool;
            _draggedColumn = null;
            PreviewDrop(point);
        }

        private void BeginColumn(int column, Point point)
        {
            _draggedColumn = column;
            _draggedTool = null;
            PreviewDrop(point);
        }

        private int ColumnAt(Point point)
        {
            for (var index = 0; index < _columnCards.Count; index++)
            {
                var bounds = ToolbarDragHandle.BoundsIn(_columnCards[index], this);
                if (point.X >= bounds.Left - 4 && point.X <= bounds.Right + 4) return index;
            }

            return -1;
        }

        private int ColumnInsertion(Point point)
        {
            for (var index = 0; index < _columnCards.Count; index++)
                if (point.X < ToolbarDragHandle.BoundsIn(_columnCards[index], this).Center.X)
                    return index;
            return _columnCards.Count;
        }

        private int ToolInsertion(int column, Point point)
        {
            var cells = _toolCells[column];
            for (var index = 0; index < cells.Count; index++)
                if (point.Y < ToolbarDragHandle.BoundsIn(cells[index], this).Center.Y)
                    return index;
            return cells.Count;
        }

        private void PreviewDrop(Point point)
        {
            var scrollBounds = ToolbarDragHandle.BoundsIn(_columnScroll, this);
            if (scrollBounds.Contains(point))
            {
                var step = point.X < scrollBounds.Left + 24 ? -20 : point.X > scrollBounds.Right - 24 ? 20 : 0;
                _columnScroll.Offset = new(Math.Max(0, _columnScroll.Offset.X + step), _columnScroll.Offset.Y);
            }

            var target = _draggedColumn is not null ? ColumnInsertion(point) : ColumnAt(point);
            for (var index = 0; index < _columnCards.Count; index++)
            {
                _columnCards[index].BorderBrush = scrollBounds.Contains(point) && index == target
                    ? AppTheme.Accent
                    : AppTheme.Outline;
                _columnCards[index].Opacity = _draggedColumn == index ? .5 : 1;
                foreach (var cell in _toolCells[index])
                    cell.Opacity = cell.Tag is ScreenshotTool tool && tool == _draggedTool ? .5 : 1;
            }

            foreach (var cell in _pool.Children)
                cell.Opacity = cell.Tag is ScreenshotTool tool && tool == _draggedTool ? .5 : 1;

            _poolArea.BorderBrush = ToolbarDragHandle.BoundsIn(_poolArea, this).Contains(point)
                ? AppTheme.Accent
                : AppTheme.Outline;
            _newColumn.BorderBrush = ToolbarDragHandle.BoundsIn(_newColumn, this).Contains(point)
                ? AppTheme.Accent
                : AppTheme.Outline;
        }

        private void Drop(Point point)
        {
            if (!new Rect(ClientSize).Contains(point))
            {
                CancelDrag();
                return;
            }

            var pool = ToolbarDragHandle.BoundsIn(_poolArea, this).Contains(point);
            var inColumns = ToolbarDragHandle.BoundsIn(_columnScroll, this).Contains(point);
            var newColumn = inColumns && ToolbarDragHandle.BoundsIn(_newColumn, this).Contains(point);
            if (_draggedColumn is { } from && (pool || inColumns))
            {
                var target = ColumnInsertion(point);
                var column = _draft.Columns[from];
                _draft.Columns.RemoveAt(from);
                if (!pool)
                {
                    if (from < target) target--;
                    _draft.Columns.Insert(Math.Clamp(target, 0, _draft.Columns.Count), column);
                }
            }
            else if (_draggedTool is { } tool && (pool || newColumn || inColumns))
            {
                var target = ColumnAt(point);
                if (pool || newColumn || target >= 0)
                {
                    var insert = target >= 0 ? ToolInsertion(target, point) : 0;
                    for (var index = 0; index < _draft.Columns.Count; index++)
                    {
                        var position = _draft.Columns[index].IndexOf(tool);
                        if (position < 0) continue;
                        _draft.Columns[index].RemoveAt(position);
                        if (index == target && position < insert) insert--;
                    }

                    if (!pool)
                    {
                        if (newColumn) _draft.Columns.Add([tool]);
                        else _draft.Columns[target].Insert(Math.Clamp(insert, 0, _draft.Columns[target].Count), tool);
                    }

                    _draft.Columns.RemoveAll(column => column.Count == 0);
                }
            }

            CancelDrag();
            RenderColumns();
        }

        private void CancelDrag()
        {
            _draggedColumn = null;
            _draggedTool = null;
            foreach (var column in _columnCards)
            {
                column.Opacity = 1;
                column.BorderBrush = AppTheme.Outline;
            }

            foreach (var cells in _toolCells)
            foreach (var cell in cells)
                cell.Opacity = 1;
            foreach (var cell in _pool.Children) cell.Opacity = 1;

            _poolArea.BorderBrush = AppTheme.Outline;
            _newColumn.BorderBrush = AppTheme.Outline;
        }
    }
}

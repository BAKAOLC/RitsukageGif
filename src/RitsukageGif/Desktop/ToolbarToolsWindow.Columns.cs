using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ToolbarToolsWindow
    {
        private void RenderColumns()
        {
            _columns.Children.Clear();
            _columnCards.Clear();
            _toolCells.Clear();
            for (var index = 0; index < _draft.Columns.Count; index++)
            {
                var columnIndex = index;
                var content = new StackPanel { Spacing = 2, Margin = new(4) };
                var header = new ToolbarDragHandle(this, point => BeginColumn(columnIndex, point), PreviewDrop,
                    Drop, CancelDrag) { Width = 48, Height = 20 };
                ToolTip.SetTip(header, UiText.Get("ToolbarDragColumn"));
                content.Children.Add(header);
                var cells = new List<ToolbarDragHandle>();
                foreach (var tool in _draft.Columns[index])
                {
                    var cell = ToolCell(tool);
                    if (cells.Count == 0)
                    {
                        cell.BorderBrush = AppTheme.Accent;
                        cell.BorderThickness = new(1);
                        ToolTip.SetTip(cell, UiText.Get(ScreenshotToolCatalog.Get(tool).NameKey) + "\n" +
                                             UiText.Get("ToolbarPrimaryToolHint"));
                    }

                    content.Children.Add(cell);
                    cells.Add(cell);
                }

                _toolCells.Add(cells);
                var card = new Border
                {
                    Width = 64, MinHeight = 112, Child = content, BorderBrush = AppTheme.Outline,
                    BorderThickness = new(1), CornerRadius = new(4), Background = AppTheme.Surface,
                };
                _columnCards.Add(card);
                _columns.Children.Add(card);
            }

            _columns.Children.Add(_newColumn);
            _pool.Children.Clear();
            var included = _draft.Columns.SelectMany(column => column).ToHashSet();
            foreach (var definition in ScreenshotToolCatalog.All)
                if (!included.Contains(definition.Tool))
                {
                    var cell = ToolCell(definition.Tool);
                    cell.Width = 36;
                    _pool.Children.Add(cell);
                }
        }

        private ToolbarDragHandle ToolCell(ScreenshotTool tool)
        {
            var definition = ScreenshotToolCatalog.Get(tool);
            var cell = new ToolbarDragHandle(this, point => BeginTool(tool, point), PreviewDrop, Drop, CancelDrag)
            {
                Width = 48, Height = 32, CornerRadius = new(3),
                Child = ToolbarDragHandle.Icon(definition.IconKey), Tag = tool,
            };
            ToolTip.SetTip(cell, UiText.Get(definition.NameKey));
            return cell;
        }
    }
}

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ToolbarActionsWindow
    {
        private void RenderRows()
        {
            _rows.Children.Clear();
            _actionRows.Clear();
            foreach (var action in _draft.Actions)
            {
                var definition = ToolbarActionCatalog.Get(action.Id);
                var row = new Border
                {
                    Height = 40, Tag = action.Id, Background = AppTheme.Surface, BorderBrush = Brushes.Transparent,
                };
                var content = new Grid
                    { ColumnDefinitions = new("18,24,*,140"), ColumnSpacing = 8, Margin = new(4, 0) };
                var handle = new ToolbarDragHandle(this, point => DragAction(action.Id, point), PreviewAction,
                    DropAction, CancelDrag);
                ToolTip.SetTip(handle, UiText.Get("ToolbarDragAction"));
                content.Children.Add(handle);
                var icon = ToolbarDragHandle.Icon(definition.IconKey);
                Grid.SetColumn(icon, 1);
                content.Children.Add(icon);
                var name = new TextBlock
                {
                    Text = UiText.Get(definition.NameKey), VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                ToolTip.SetTip(name, name.Text);
                Grid.SetColumn(name, 2);
                content.Children.Add(name);
                var visibility = new ComboBox
                {
                    ItemsSource = new[]
                    {
                        UiText.Get("ToolbarVisibilityToolbar"), UiText.Get("ToolbarVisibilityMoreMenu"),
                        UiText.Get("ToolbarVisibilityHidden"),
                    },
                    SelectedIndex = (int)action.Visibility, Height = 28, MinHeight = 0, Width = 140,
                    Padding = new(6, 0),
                    VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                };
                visibility.SelectionChanged += (_, _) =>
                {
                    if (visibility.SelectedIndex >= 0)
                        action.Visibility = (ToolbarButtonVisibility)visibility.SelectedIndex;
                };
                Grid.SetColumn(visibility, 3);
                content.Children.Add(visibility);
                row.Child = content;
                _rows.Children.Add(row);
                _actionRows.Add(row);
            }
        }
    }
}

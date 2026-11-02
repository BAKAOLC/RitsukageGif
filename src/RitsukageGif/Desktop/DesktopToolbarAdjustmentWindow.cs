using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class DesktopToolbarAdjustmentWindow : Window
    {
        private readonly Action<DesktopToolbarPreferences> _apply;
        private readonly Border[] _cards = [new(), new(), new()];
        private readonly List<string>[] _groups;
        private readonly StackPanel[] _lists = [new(), new(), new()];
        private readonly DesktopToolbarPreferences _preferences;
        private readonly List<Control>[] _rows = [[], [], []];
        private string? _dragged;
        private int _dropGroup = -1;
        private int _dropIndex;

        internal DesktopToolbarAdjustmentWindow(DesktopToolbarPreferences preferences,
            Action<DesktopToolbarPreferences> apply)
        {
            _preferences = preferences.Normalize();
            _apply = apply;
            _groups =
            [
                [.. _preferences.Primary], [.. _preferences.More],
                [.. DesktopToolbarActions.Ids.Except(_preferences.Primary).Except(_preferences.More)],
            ];
            Title = UiText.Get("DesktopToolbarAdjust");
            Width = 630;
            Height = 360;
            MinWidth = 540;
            MinHeight = 260;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Workspace;
            Foreground = AppTheme.Ink;
            var layout = new Grid { RowDefinitions = new("*,Auto"), Margin = new(10), RowSpacing = 8 };
            var columns = new Grid { ColumnDefinitions = new("*,*,*"), ColumnSpacing = 8 };
            var titles = new[] { "DesktopToolbarPrimary", "DesktopToolbarMore", "DesktopToolbarHidden" };
            for (var index = 0; index < _groups.Length; index++)
            {
                var column = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 6 };
                column.Children.Add(new TextBlock { Text = UiText.Get(titles[index]) });
                _cards[index].Background = AppTheme.Surface;
                _cards[index].BorderBrush = AppTheme.Outline;
                _cards[index].BorderThickness = new(1);
                _cards[index].CornerRadius = new(4);
                _cards[index].Padding = new(4);
                _cards[index].Child = new ScrollViewer { Content = _lists[index] };
                Grid.SetRow(_cards[index], 1);
                column.Children.Add(_cards[index]);
                Grid.SetColumn(column, index);
                columns.Children.Add(column);
            }

            layout.Children.Add(columns);
            var footer = new Grid { ColumnDefinitions = new("Auto,*,Auto") };
            footer.Children.Add(ToolbarDragHandle.Command("ToolbarRestoreDefaults", RestoreDefaults));
            var close = ToolbarDragHandle.Command("Close", Close);
            Grid.SetColumn(close, 2);
            footer.Children.Add(close);
            Grid.SetRow(footer, 1);
            layout.Children.Add(footer);
            Content = layout;
            KeyDown += (_, args) =>
            {
                if (args.Key != Key.Escape) return;
                if (_dragged is null) Close();
                else CancelDrag();
                args.Handled = true;
            };
            RenderRows();
        }

        private void RestoreDefaults()
        {
            var defaults = new DesktopToolbarPreferences();
            _groups[0].Clear();
            _groups[0].AddRange(defaults.Primary);
            _groups[1].Clear();
            _groups[1].AddRange(defaults.More);
            _groups[2].Clear();
            _groups[2].AddRange(DesktopToolbarActions.Ids.Except(defaults.Primary).Except(defaults.More));
            RenderRows();
            Apply();
        }

        private void RenderRows()
        {
            for (var group = 0; group < _groups.Length; group++)
            {
                _lists[group].Children.Clear();
                _rows[group].Clear();
                foreach (var id in _groups[group])
                {
                    var row = new ToolbarDragHandle(this, _ => _dragged = id, PreviewDrop, Drop, CancelDrag)
                    {
                        Width = double.NaN, Height = 30, HorizontalAlignment = HorizontalAlignment.Stretch,
                    };
                    var content = new Grid { ColumnDefinitions = new("Auto,Auto,*"), ColumnSpacing = 5 };
                    content.Children.Add(ToolbarDragHandle.Icon("ToolbarMove"));
                    var icon = ToolbarDragHandle.Icon(DesktopToolbarActions.Definition(id).IconKey);
                    Grid.SetColumn(icon, 1);
                    content.Children.Add(icon);
                    var label = new TextBlock
                    {
                        Text = UiText.Get(DesktopToolbarActions.NameKey(id)),
                        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                    };
                    Grid.SetColumn(label, 2);
                    content.Children.Add(label);
                    ToolTip.SetTip(row, label.Text);
                    row.Child = content;
                    _lists[group].Children.Add(row);
                    _rows[group].Add(row);
                }
            }
        }

        private void PreviewDrop(Point point)
        {
            _dropGroup = -1;
            for (var group = 0; group < _groups.Length; group++)
            {
                var inside = ToolbarDragHandle.BoundsIn(_cards[group], this).Contains(point);
                _cards[group].BorderBrush = inside ? AppTheme.Accent : AppTheme.Outline;
                if (!inside) continue;
                _dropGroup = group;
                _dropIndex = _rows[group].FindIndex(row => point.Y < ToolbarDragHandle.BoundsIn(row, this).Center.Y);
                if (_dropIndex < 0) _dropIndex = _rows[group].Count;
            }
        }

        private void Drop(Point point)
        {
            PreviewDrop(point);
            if (_dragged is not { } id || _dropGroup < 0)
            {
                CancelDrag();
                return;
            }

            var previous = _groups[_dropGroup].IndexOf(id);
            if (previous >= 0 && previous < _dropIndex) _dropIndex--;
            foreach (var group in _groups) group.Remove(id);
            _groups[_dropGroup].Insert(_dropIndex, id);
            CancelDrag();
            RenderRows();
            Apply();
        }

        private void Apply()
        {
            _apply(_preferences with { Primary = [.. _groups[0]], More = [.. _groups[1]] });
        }

        private void CancelDrag()
        {
            _dragged = null;
            _dropGroup = -1;
            foreach (var card in _cards) card.BorderBrush = AppTheme.Outline;
        }
    }
}

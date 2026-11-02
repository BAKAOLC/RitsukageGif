using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed class ColorFormatsWindow : Window
    {
        private readonly List<Border> _cards = [];
        private readonly List<ColorFormatDefinition> _definitions;
        private readonly StackPanel _rows = new() { Spacing = 0 };
        private int _dragged = -1;

        public ColorFormatsWindow(IEnumerable<ColorFormatDefinition> definitions)
        {
            _definitions = SampleColorFormats.Normalize(definitions);
            Title = UiText.Get("CaptureColorFormatsManage");
            Width = 720;
            Height = 430;
            MinWidth = 480;
            MinHeight = 280;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Workspace;
            Foreground = AppTheme.Ink;
            var layout = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new(12), RowSpacing = 8 };
            layout.Children.Add(new TextBlock
                { Text = UiText.Get("CaptureColorFormatsHint"), Foreground = AppTheme.Muted, FontSize = 12 });
            var scroll = new ScrollViewer
                { Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetRow(scroll, 1);
            layout.Children.Add(scroll);
            var footer = new Grid { ColumnDefinitions = new("Auto,Auto,*,Auto,Auto"), ColumnSpacing = 6 };
            footer.Children.Add(ToolbarDragHandle.Command("CaptureColorFormatAdd", () => _ = EditAsync(-1)));
            var restore = ToolbarDragHandle.Command("ToolbarRestoreDefaults", () =>
            {
                _definitions.Clear();
                _definitions.AddRange(SampleColorFormats.CreateDefaults());
                RenderRows();
            });
            Grid.SetColumn(restore, 1);
            footer.Children.Add(restore);
            var cancel = ToolbarDragHandle.Command("Cancel", () => Close(null));
            Grid.SetColumn(cancel, 3);
            footer.Children.Add(cancel);
            var save = ToolbarDragHandle.Command("ImageSave", () => Close(SampleColorFormats.Normalize(_definitions)));
            save.Classes.Add("primary");
            Grid.SetColumn(save, 4);
            footer.Children.Add(save);
            Grid.SetRow(footer, 2);
            layout.Children.Add(footer);
            Content = layout;
            AddHandler(KeyDownEvent, (_, args) =>
            {
                if (args is { Handled: false, Key: Key.Escape })
                {
                    Close(null);
                    args.Handled = true;
                }
            }, RoutingStrategies.Bubble);
            RenderRows();
        }

        private void RenderRows()
        {
            _rows.Children.Clear();
            _cards.Clear();
            for (var index = 0; index < _definitions.Count; index++)
            {
                var rowIndex = index;
                var definition = _definitions[index];
                var row = new Grid { ColumnDefinitions = new("20,28,120,*,28,28"), ColumnSpacing = 6, MinHeight = 34 };
                var grip = new ToolbarDragHandle(this, _ => _dragged = rowIndex, PreviewDrag, Drop, CancelDrag);
                row.Children.Add(grip);
                var enabled = new CheckBox
                {
                    IsChecked = definition.Enabled, VerticalAlignment = VerticalAlignment.Center, MinHeight = 0,
                    Padding = new(0),
                };
                enabled.IsCheckedChanged += (_, _) =>
                    _definitions[rowIndex] = _definitions[rowIndex] with { Enabled = enabled.IsChecked == true };
                Add(enabled, 1);
                Add(
                    new TextBlock
                    {
                        Text = definition.Name, VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    }, 2);
                Add(
                    new TextBlock
                    {
                        Text = definition.Template, FontFamily = FontFamily.Default,
                        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                        Foreground = AppTheme.Muted,
                    }, 3);
                var edit = IconButton("ScrollCaptureEdit", "CaptureColorFormatEdit");
                edit.Click += async (_, _) => await EditAsync(rowIndex);
                Add(edit, 4);
                var delete = IconButton("ScreenshotDeleteAnnotation", "CaptureColorFormatDelete");
                delete.Click += (_, _) =>
                {
                    _definitions.RemoveAt(rowIndex);
                    RenderRows();
                };
                Add(delete, 5);
                var card = new Border
                {
                    Child = row, BorderBrush = AppTheme.Outline, BorderThickness = new(0, 0, 0, 1), Padding = new(4, 2),
                };
                card.DoubleTapped += async (_, args) =>
                {
                    if (args.Source is Control source &&
                        (source is Button || source.FindAncestorOfType<Button>() is not null)) return;
                    await EditAsync(rowIndex);
                    args.Handled = true;
                };
                var menu = new ContextMenu();
                var editItem = new MenuItem { Header = UiText.Get("CaptureColorFormatEdit") };
                editItem.Click += async (_, _) => await EditAsync(rowIndex);
                var removeItem = new MenuItem { Header = UiText.Get("CaptureColorFormatDelete") };
                removeItem.Click += (_, _) =>
                {
                    _definitions.RemoveAt(rowIndex);
                    RenderRows();
                };
                menu.Items.Add(editItem);
                menu.Items.Add(removeItem);
                card.ContextMenu = menu;
                _cards.Add(card);
                _rows.Children.Add(card);
                continue;

                void Add(Control control, int column)
                {
                    Grid.SetColumn(control, column);
                    row.Children.Add(control);
                }
            }
        }

        private async Task EditAsync(int index)
        {
            var original = index < 0
                ? new() { Id = Guid.NewGuid().ToString("N"), Name = "", Template = "rgb(%Re, %Gr, %Bl)" }
                : _definitions[index];
            var editor = new ColorFormatEditorWindow(original);
            if (await editor.ShowDialog<ColorFormatDefinition?>(this) is not { } value) return;
            if (index < 0) _definitions.Add(value);
            else _definitions[index] = value;
            RenderRows();
        }

        private void PreviewDrag(Point point)
        {
            var target = TargetAt(point);
            for (var i = 0; i < _cards.Count; i++)
                _cards[i].Background = i == target ? AppTheme.SurfaceTint : Brushes.Transparent;
        }

        private int TargetAt(Point point)
        {
            for (var i = 0; i < _cards.Count; i++)
                if (ToolbarDragHandle.BoundsIn(_cards[i], this).Contains(point))
                    return i;
            return -1;
        }

        private void Drop(Point point)
        {
            var target = TargetAt(point);
            if (_dragged >= 0 && target >= 0 && _dragged != target)
            {
                var item = _definitions[_dragged];
                _definitions.RemoveAt(_dragged);
                _definitions.Insert(target, item);
            }

            CancelDrag();
            RenderRows();
        }

        private void CancelDrag()
        {
            _dragged = -1;
            foreach (var card in _cards) card.Background = Brushes.Transparent;
        }

        private static Button IconButton(string icon, string name)
        {
            var button = new Button
            {
                Width = 28, Height = 28, MinHeight = 0, MinWidth = 0,
                Padding = new(2), Background = Brushes.Transparent,
            };
            button.Content = ToolbarIcon.Create(button, ToolbarIcon.Geometry(icon));
            AutomationProperties.SetName(button, UiText.Get(name));
            ToolTip.SetTip(button, UiText.Get(name));
            return button;
        }
    }
}

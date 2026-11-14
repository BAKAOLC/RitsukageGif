using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ToolbarActionsWindow : Window
    {
        private readonly List<Border> _actionRows = [];
        private readonly StackPanel _rows = new() { Spacing = 0 };
        private readonly ScrollViewer _scroll;
        private ToolbarPreferences _draft;
        private ToolbarActionId? _dragged;

        public ToolbarActionsWindow(ToolbarPreferences preferences)
        {
            _draft = preferences.Normalize();
            Title = UiText.Get("ToolbarActionsTitle");
            Width = 450;
            Height = 640;
            MinWidth = 450;
            MinHeight = 260;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Workspace;
            Foreground = AppTheme.Ink;
            var layout = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new(12), RowSpacing = 8 };
            layout.Children.Add(new TextBlock
            {
                Text = UiText.Get("ToolbarActionsDragHint"), TextWrapping = TextWrapping.Wrap, FontSize = 12,
                Foreground = AppTheme.Muted,
            });
            _scroll = new()
            {
                Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            Grid.SetRow(_scroll, 1);
            layout.Children.Add(_scroll);
            var footer = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto"), ColumnSpacing = 8 };
            footer.Children.Add(ToolbarDragHandle.Command("ToolbarRestoreDefaults", () =>
            {
                _draft = _draft with { Actions = new ToolbarPreferences().Actions };
                RenderRows();
            }));
            var cancel = ToolbarDragHandle.Command("Cancel", () => Close(null));
            Grid.SetColumn(cancel, 2);
            footer.Children.Add(cancel);
            var save = ToolbarDragHandle.Command("ImageSave", () => Close(_draft.Normalize()));
            save.Classes.Add("primary");
            Grid.SetColumn(save, 3);
            footer.Children.Add(save);
            Grid.SetRow(footer, 2);
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
                MaxHeight = Math.Max(260, screen.WorkingArea.Height / RenderScaling - 48);
                Height = Math.Min(Height, MaxHeight);
            };
            RenderRows();
        }

        private int InsertionAt(Point point)
        {
            for (var index = 0; index < _actionRows.Count; index++)
                if (point.Y < ToolbarDragHandle.BoundsIn(_actionRows[index], this).Center.Y)
                    return index;
            return _actionRows.Count;
        }

        private void DragAction(ToolbarActionId id, Point point)
        {
            _dragged = id;
            PreviewAction(point);
        }

        private void PreviewAction(Point point)
        {
            var bounds = ToolbarDragHandle.BoundsIn(_scroll, this);
            if (bounds.Contains(point))
            {
                var step = point.Y < bounds.Top + 24 ? -20 : point.Y > bounds.Bottom - 24 ? 20 : 0;
                _scroll.Offset = new(_scroll.Offset.X, Math.Max(0, _scroll.Offset.Y + step));
            }

            var insertion = InsertionAt(point);
            for (var index = 0; index < _actionRows.Count; index++)
            {
                var row = _actionRows[index];
                row.Opacity = row.Tag is ToolbarActionId id && id == _dragged ? .5 : 1;
                row.BorderBrush = index == Math.Min(insertion, _actionRows.Count - 1)
                    ? AppTheme.Accent
                    : Brushes.Transparent;
                row.BorderThickness = insertion == _actionRows.Count ? new(0, 0, 0, 2) : new(0, 2, 0, 0);
            }
        }

        private void DropAction(Point point)
        {
            if (_dragged is { } id && ToolbarDragHandle.BoundsIn(_scroll, this).Contains(point))
            {
                var from = _draft.Actions.FindIndex(action => action.Id == id);
                var target = InsertionAt(point);
                var action = _draft.Actions[from];
                if (from < target) target--;
                target = Math.Clamp(target, 0, _draft.Actions.Count - 1);
                _draft.Actions.RemoveAt(from);
                _draft.Actions.Insert(target, action);
                var previous = target == 0 ? null : (ToolbarButtonVisibility?)_draft.Actions[target - 1].Visibility;
                var next = target + 1 < _draft.Actions.Count
                    ? _draft.Actions[target + 1].Visibility
                    : (ToolbarButtonVisibility?)null;
                action.Visibility = DropVisibility(action.Visibility, previous, next);
            }

            CancelDrag();
            RenderRows();
        }

        private static ToolbarButtonVisibility DropVisibility(ToolbarButtonVisibility current,
            ToolbarButtonVisibility? previous, ToolbarButtonVisibility? next)
        {
            if (previous is null) return next ?? ToolbarButtonVisibility.Toolbar;
            if (next is null) return current;
            if (previous == next || (current != previous && current != next)) return previous.Value;
            return current;
        }

        private void ChangeVisibility(ToolbarActionPreference action, ToolbarButtonVisibility visibility)
        {
            if (action.Visibility == visibility) return;
            action.Visibility = visibility;
            _draft.Actions.Remove(action);
            var target = _draft.Actions.FindLastIndex(row => row.Visibility == visibility);
            if (target < 0)
                target = _draft.Actions.FindLastIndex(row => row.Visibility < visibility);
            _draft.Actions.Insert(target + 1, action);
            RenderRows();
        }

        private void CancelDrag()
        {
            _dragged = null;
            foreach (var row in _actionRows)
            {
                row.Opacity = 1;
                row.BorderBrush = Brushes.Transparent;
                row.BorderThickness = new(0);
            }
        }
    }
}

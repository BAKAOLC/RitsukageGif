using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedGroupsManagerWindow
    {
        private readonly DispatcherTimer _groupDragTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
        private readonly List<ToolbarDragHandle> _groupRows = [];
        private string? _draggedGroup;
        private Point _groupDragPoint;

        private void InitializeGroupReordering()
        {
            _groupDragTimer.Tick += (_, _) => PreviewGroupDrop(_groupDragPoint);
            AddHandler(KeyDownEvent, (_, args) =>
            {
                if (args.Key != Key.Escape || _draggedGroup is null) return;
                CancelGroupDrag();
                args.Handled = true;
            }, RoutingStrategies.Tunnel);
            Closed += (_, _) => CancelGroupDrag();
        }

        private ToolbarDragHandle GroupDragRow(Grid content, string id, ListBoxItem item)
        {
            item.AddHandler(PointerPressedEvent, (_, args) =>
            {
                if (!args.GetCurrentPoint(item).Properties.IsLeftButtonPressed) return;
                _groups.SelectedItem = item;
                _groups.Focus();
            }, RoutingStrategies.Tunnel);
            var row = new ToolbarDragHandle(_groups, origin =>
            {
                _draggedGroup = id;
                _groupDragPoint = origin;
                _groupDragTimer.Start();
            }, PreviewGroupDrop, DropGroup, CancelGroupDrag)
            {
                Width = double.NaN, Height = double.NaN, Cursor = null, Tag = id,
                BorderThickness = new(0, 2, 0, 0), BorderBrush = Brushes.Transparent, Child = content,
            };
            _groupRows.Add(row);
            return row;
        }

        private int GroupInsertionAt(Point point)
        {
            var insertion = 0;
            for (var index = 0; index < _groupRows.Count; index++)
            {
                var row = _groupRows[index];
                if (row.GetPresentationSource() is null) continue;
                if (point.Y < ToolbarDragHandle.BoundsIn(row, _groups).Center.Y) return index;
                insertion = index + 1;
            }

            return insertion;
        }

        private void PreviewGroupDrop(Point point)
        {
            if (_draggedGroup is null) return;
            _groupDragPoint = point;
            if (_groups.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroll)
            {
                var step = point.Y < 24 ? -20 : point.Y > _groups.Bounds.Height - 24 ? 20 : 0;
                scroll.Offset = new(scroll.Offset.X, Math.Max(0, scroll.Offset.Y + step));
            }

            var insertion = GroupInsertionAt(point);
            for (var index = 0; index < _groupRows.Count; index++)
            {
                var row = _groupRows[index];
                row.Opacity = row.Tag as string == _draggedGroup ? .5 : 1;
                row.BorderBrush = index == Math.Min(insertion, _groupRows.Count - 1)
                    ? AppTheme.Accent
                    : Brushes.Transparent;
                row.BorderThickness = insertion == _groupRows.Count ? new(0, 0, 0, 2) : new(0, 2, 0, 0);
            }
        }

        private void DropGroup(Point point)
        {
            if (_draggedGroup is { } id && new Rect(_groups.Bounds.Size).Contains(point))
            {
                var ids = _groups.Items.Cast<ListBoxItem>().Select(item => (string)item.Tag!).ToList();
                var from = ids.IndexOf(id);
                var target = GroupInsertionAt(point);
                ids.RemoveAt(from);
                if (from < target) target--;
                ids.Insert(target, id);
                _manager.ReorderGroups(ids);
            }

            CancelGroupDrag();
        }

        private void CancelGroupDrag()
        {
            _groupDragTimer.Stop();
            _draggedGroup = null;
            foreach (var row in _groupRows)
            {
                row.Opacity = 1;
                row.BorderBrush = Brushes.Transparent;
                row.BorderThickness = new(0, 2, 0, 0);
            }
        }
    }
}

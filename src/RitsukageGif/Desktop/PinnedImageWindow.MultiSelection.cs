using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        public Func<PinnedImageWindow, int>? SelectionCountProvider { get; set; }
        public Func<PinnedImageWindow, PinnedImageArrangement, bool>? CanArrangeSelectionProvider { get; set; }
        public Action<PinnedImageWindow, PinnedImageArrangement>? ArrangeSelectionRequested { get; set; }
        public Func<PinnedImageWindow, Task>? SaveSelectionRequested { get; set; }
        public Func<PinnedImageWindow, bool>? CanSaveSelectionProvider { get; set; }
        internal bool CanArrangeSelection => !_closed && !IsInteractionLocked && _annotation is null;

        internal PixelRect SelectionLayoutBounds
        {
            get
            {
                var width = (int)Math.Clamp(Math.Ceiling(Math.Max(ClientSize.Width, Width) * RenderScaling), 1,
                    int.MaxValue);
                var height = (int)Math.Clamp(Math.Ceiling(Math.Max(ClientSize.Height, Height) * RenderScaling), 1,
                    int.MaxValue);
                return new(Position.X, Position.Y, width, height);
            }
        }

        private void AddMultiSelectionMenu(ContextMenu menu)
        {
            var selection = new MenuItem();
            var align = new MenuItem
                { [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedSelectionAlign") };
            var distribute = new MenuItem
                { [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedSelectionDistribute") };
            var compact = new MenuItem
                { [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedSelectionCompact") };
            var items = new List<(MenuItem Item, PinnedImageArrangement Arrangement)>();
            foreach (var (arrangement, action, textKey) in PinnedImageArrangementCommands.Catalog)
            {
                var item = Command(textKey, () =>
                {
                    ArrangeSelectionRequested?.Invoke(this, arrangement);
                    return Task.CompletedTask;
                });
                item.Tag = action;
                item.Icon = PinnedSelectionIcon(item);
                items.Add((item, arrangement));
                var parent = arrangement switch
                {
                    PinnedImageArrangement.DistributeHorizontal
                        or PinnedImageArrangement.DistributeVertical => distribute,
                    PinnedImageArrangement.Compact or PinnedImageArrangement.CompactHorizontal
                        or PinnedImageArrangement.CompactVertical => compact,
                    _ => align,
                };
                parent.Items.Add(item);
            }

            selection.Items.Add(align);
            selection.Items.Add(distribute);
            selection.Items.Add(compact);
            selection.Items.Add(new Separator());
            var save = Command("PinnedSelectionSave", () => SaveSelectionRequested?.Invoke(this) ?? Task.CompletedTask);
            save.Tag = "selection-save";
            selection.Items.Add(save);
            menu.Items.Add(selection);
            menu.Opened += (_, _) =>
            {
                var count = SelectionCountProvider?.Invoke(this) ?? 0;
                selection.IsVisible = count >= 2;
                selection.Header = UiText.Format("PinnedSelectionCount", count);
                foreach (var (item, arrangement) in items)
                {
                    item.IsEnabled = CanArrangeSelectionProvider?.Invoke(this, arrangement) == true;
                    item.InputGesture = CurrentPreferences.ShowMenuShortcuts
                        ? BuiltinShortcuts.Snapshot().FirstOrDefault(binding =>
                            binding.ContextKey == "BuiltinShortcutPinnedContext" &&
                            binding.Action == (string)item.Tag!)?.Gesture
                        : null;
                }

                save.IsEnabled = CanSaveSelectionProvider?.Invoke(this) == true;
                save.InputGesture = CurrentPreferences.ShowMenuShortcuts
                    ? BuiltinShortcuts.Snapshot().FirstOrDefault(binding => binding is
                        { ContextKey: "BuiltinShortcutPinnedContext", Action: "selection-save" })?.Gesture
                    : null;
            };
        }

        private static Path PinnedSelectionIcon(MenuItem item)
        {
            return new()
            {
                Data = Geometry.Parse("M3,3 V21 M7,6 H19 V9 H7 Z M7,14 H15 V17 H7 Z"),
                [!Shape.StrokeProperty] = item[!ForegroundProperty],
                StrokeThickness = 1.5, Width = 16, Height = 16,
                Stretch = Stretch.Uniform,
            };
        }

        private bool ExecuteMultiSelectionShortcut(string action)
        {
            if (action == "selection-save")
            {
                _ = RunAsync(() => SaveSelectionRequested?.Invoke(this) ?? Task.CompletedTask);
                return true;
            }

            if (PinnedImageArrangementCommands.TryResolve(action, out var arrangement))
            {
                _ = RunAsync(() =>
                {
                    ArrangeSelectionRequested?.Invoke(this, arrangement);
                    return Task.CompletedTask;
                });
                return true;
            }

            return false;
        }
    }
}

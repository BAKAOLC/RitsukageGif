using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private ContextMenu? CreateSelectionMenu(IPinnedWindow source)
        {
            var selected = SelectionSnapshot(source);
            if (selected.Length < 2) return null;
            var menu = new ContextMenu { Name = "PinnedSelectionMenu" };
            source.Presentation.ConnectMenuOrigin(menu);
            menu.Items.Add(new MenuItem
            {
                Header = UiText.Format("PinnedSelectionNativeCount", selected.Length),
                IsEnabled = false, Tag = "selection-count",
            });
            menu.Items.Add(new Separator());
            menu.Items.Add(
                Command("PinnedSelectionDisplayedSaveAs", () => SaveSelectionAsync(source), "selection-save"));
            menu.Items.Add(new Separator());
            var hideOthers = Command("PinnedMenuHideOthers", () =>
            {
                ToggleOtherPins(source);
                return Task.CompletedTask;
            }, "hide-others");
            hideOthers.ToggleType = MenuItemToggleType.CheckBox;
            hideOthers.IsChecked = AllPinsHidden;
            menu.Items.Add(hideOthers);
            var alignment = new MenuItem { Header = UiText.Get("PinnedSelectionNativeAlign"), Tag = "selection-align" };
            foreach (var operation in new[]
                     {
                         PinnedImageArrangement.AlignTop, PinnedImageArrangement.AlignMiddle,
                         PinnedImageArrangement.AlignBottom, PinnedImageArrangement.AlignLeft,
                         PinnedImageArrangement.AlignRight,
                     })
                AddArrangement(operation);
            alignment.Items.Add(new Separator());
            AddArrangement(PinnedImageArrangement.DistributeHorizontal);
            AddArrangement(PinnedImageArrangement.DistributeVertical);
            AddArrangement(PinnedImageArrangement.Compact);
            menu.Items.Add(alignment);
            var group = new MenuItem { Header = UiText.Get("PinnedMoveToGroup"), Tag = "move-group" };
            foreach (var definition in GroupDefinitions)
            {
                var current = definition.Id == source.Group;
                var item = new MenuItem
                {
                    Header = source.Presentation.CreateGroupMenuHeader(definition),
                    IsEnabled = !current,
                    ToggleType = definition.Color is null && current
                        ? MenuItemToggleType.CheckBox
                        : MenuItemToggleType.None,
                    IsChecked = definition.Color is null && current,
                };
                if (definition.Color is not null) item.Icon = PinnedGroupIcon.Create(item, definition, current);
                item.Click += (_, _) => SetSelectionGroup(source, definition.Id);
                group.Items.Add(item);
            }

            group.Items.Add(new Separator());
            group.Items.Add(Command("PinnedMoveToNewGroup", () => CreateAndMoveToGroupAsync(source), "new-group"));
            menu.Items.Add(group);
            var locked = new MenuItem
            {
                Header = UiText.Get("PinnedMenuLock"), ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = selected.All(window => window.IsLocked),
            };
            ConnectCommand(locked, () =>
            {
                foreach (var window in SelectionSnapshot(source)) window.SetLocked(locked.IsChecked);
                return Task.CompletedTask;
            }, "lock");
            menu.Items.Add(locked);
            menu.Items.Add(Command("Close", () =>
            {
                CloseSelection(source, false);
                return Task.CompletedTask;
            }, "close"));
            menu.Items.Add(Command("PinnedImageDestroy", () =>
            {
                CloseSelection(source, true);
                return Task.CompletedTask;
            }, "destroy"));
            return menu;

            MenuItem Command(string key, Func<Task> operation, string action)
            {
                var item = new MenuItem { Header = UiText.Get(key) };
                ConnectCommand(item, operation, action);
                return item;
            }

            void ConnectCommand(MenuItem item, Func<Task> operation, string action)
            {
                item.Tag = action;
                if (Preferences.ShowMenuShortcuts)
                    item.InputGesture = BuiltinShortcuts.Snapshot().FirstOrDefault(binding =>
                        binding.ContextKey == "BuiltinShortcutPinnedContext" && binding.Action == action)?.Gesture;
                item.Click += async (_, _) => await source.Presentation.RunMenuAsync(() =>
                    source.Presentation.RunOperationAsync(operation));
            }

            void AddArrangement(PinnedImageArrangement operation)
            {
                var definition =
                    PinnedImageArrangementCommands.Catalog.First(command => command.Arrangement == operation);
                // ReSharper disable once SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault
                var key = operation switch
                {
                    PinnedImageArrangement.AlignTop => "PinnedSelectionNativeTop",
                    PinnedImageArrangement.AlignMiddle => "PinnedSelectionNativeCenter",
                    PinnedImageArrangement.AlignBottom => "PinnedSelectionNativeBottom",
                    PinnedImageArrangement.AlignLeft => "PinnedSelectionNativeLeft",
                    PinnedImageArrangement.AlignRight => "PinnedSelectionNativeRight",
                    PinnedImageArrangement.DistributeHorizontal => "PinnedSelectionNativeHorizontal",
                    PinnedImageArrangement.DistributeVertical => "PinnedSelectionNativeVertical",
                    PinnedImageArrangement.Compact => "PinnedSelectionNativeCompact",
                    _ => throw new ArgumentOutOfRangeException(nameof(operation)),
                };
                alignment.Items.Add(Command(key, () => ArrangeSelection(source, operation), definition.Action));
            }
        }
    }
}

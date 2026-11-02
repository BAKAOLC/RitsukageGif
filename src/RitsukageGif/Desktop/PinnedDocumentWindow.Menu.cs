using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        private MenuItem Command(string key, Func<Task> execute, string? action = null)
        {
            var item = new MenuItem { Tag = action };
            item.Bind(HeaderedSelectingItemsControl.HeaderProperty, LiveText.Resource(key));
            item.Click += (_, _) => _ = Presentation.RunMenuAsync(() => RunAsync(execute));
            return item;
        }

        private ContextMenu CreateMenu()
        {
            var menu = new ContextMenu();
            Presentation.ConnectMenu(menu);
            menu.Items.Add(Command("ScreenshotCopy", CopyAsync, "copy"));
            menu.Items.Add(Command("PinnedDocumentCopyImage", CopyRenderedImageAsync));
            menu.Items.Add(Command("PinnedImageCopyFile", CopyAsFileAsync, "copy-file"));
            menu.Items.Add(Command("ScreenshotSave", SaveDocumentAsync, "save"));
            menu.Items.Add(Command("PinnedDocumentSaveImage", SaveRenderedImageAsync));
            if (Document is PinnedTextDocument or PinnedHtmlDocument)
            {
                menu.Items.Add(new Separator());
                var selectable = Command("PinnedTextSelectable", () =>
                {
                    SetSelectable(!Selectable);
                    return Task.CompletedTask;
                });
                selectable.Tag = "text-selection";
                selectable.ToggleType = MenuItemToggleType.CheckBox;
                menu.Items.Add(selectable);
                var selectAll = Command("PinnedTextSelectAll", () =>
                {
                    _textView?.SelectAll();
                    return Task.CompletedTask;
                });
                selectAll.Tag = "text-select-all";
                menu.Items.Add(selectAll);
                MenuItem? ignore = null;
                if (Document is PinnedHtmlDocument)
                {
                    ignore = Command("PinnedTextIgnoreFormat", () =>
                    {
                        SetIgnoreFormat(!IgnoreFormat);
                        return Task.CompletedTask;
                    });
                    ignore.Tag = "text-ignore-format";
                    ignore.ToggleType = MenuItemToggleType.CheckBox;
                    menu.Items.Add(ignore);
                }

                menu.Opened += (_, _) =>
                {
                    selectable.IsChecked = Selectable;
                    selectAll.IsEnabled = Selectable;
                    ignore?.SetCurrentValue(MenuItem.IsCheckedProperty, IgnoreFormat);
                };
            }

            menu.Items.Add(new Separator());
            var locked = Command("PinnedImageLock", () =>
            {
                _locked = !_locked;
                RefreshPreferencePresentation();
                StateChanged?.Invoke(this, EventArgs.Empty);
                return Task.CompletedTask;
            }, "lock");
            locked.ToggleType = MenuItemToggleType.CheckBox;
            menu.Items.Add(locked);
            var through = Command("PinnedImageMouseThrough", () =>
            {
                SetMouseThrough(!_mouseThrough);
                return Task.CompletedTask;
            }, "through");
            through.ToggleType = MenuItemToggleType.CheckBox;
            menu.Items.Add(through);
            var topmost = Command("PinnedImageTopmost", () =>
            {
                Topmost = !Topmost;
                RefreshPreferencePresentation();
                return Task.CompletedTask;
            }, "topmost");
            topmost.ToggleType = MenuItemToggleType.CheckBox;
            menu.Items.Add(topmost);
            menu.Items.Add(Command("PinnedImageMultiSelect", () =>
            {
                SelectionRequested?.Invoke(this);
                return Task.CompletedTask;
            }, "select"));
            var group = new MenuItem();
            group.Bind(HeaderedSelectingItemsControl.HeaderProperty, LiveText.Resource("PinnedImageGroup"));
            menu.Items.Add(group);
            menu.Items.Add(Command("PinnedTitleEdit", () =>
            {
                Presentation.Title.BeginEdit();
                return Task.CompletedTask;
            }, "set-title"));
            menu.Items.Add(Command("PinnedHideOthers", () =>
            {
                Presentation.HideOthersRequested?.Invoke();
                return Task.CompletedTask;
            }, "hide-others"));
            menu.Items.Add(Command("PinnedSwitchNext", () =>
            {
                Presentation.SwitchNextRequested?.Invoke();
                return Task.CompletedTask;
            }, "switch-next"));
            var arrange = new MenuItem();
            arrange.Bind(HeaderedSelectingItemsControl.HeaderProperty, LiveText.Resource("PinnedSelectionAlign"));
            foreach (var definition in PinnedImageArrangementCommands.Catalog)
            {
                var item = Command(definition.TextKey, () =>
                {
                    ArrangeSelectionRequested?.Invoke(this, definition.Arrangement);
                    return Task.CompletedTask;
                }, definition.Action);
                arrange.Items.Add(item);
                menu.Opened += (_, _) =>
                    item.IsEnabled = CanArrangeSelectionProvider?.Invoke(this, definition.Arrangement) == true;
            }

            menu.Items.Add(arrange);
            menu.Items.Add(Command("PinnedSelectionSave",
                () => SaveSelectionRequested?.Invoke(this) ?? Task.CompletedTask,
                "selection-save"));
            menu.Items.Add(new Separator());
            menu.Items.Add(Command("PinnedImageHide", () =>
            {
                HideImage();
                return Task.CompletedTask;
            }, "hide"));
            menu.Items.Add(Command("Close", () =>
            {
                CloseRequested?.Invoke(this, false);
                return Task.CompletedTask;
            }, "close"));
            menu.Items.Add(Command("PinnedImageDestroy", () =>
            {
                CloseRequested?.Invoke(this, true);
                return Task.CompletedTask;
            }, "destroy"));
            menu.Opened += (_, _) =>
            {
                locked.IsChecked = _locked;
                through.IsChecked = _mouseThrough;
                through.IsEnabled = PinnedWindowPlatform.CanSetMouseThrough(this) &&
                                    (_mouseThrough || _canRestoreHidden());
                topmost.IsChecked = Topmost;
                group.Items.Clear();
                foreach (var definition in GroupsProvider())
                {
                    var item = new MenuItem
                    {
                        Header = definition.Name.Length == 0 ? UiText.Get("PinnedImageUngrouped") : definition.Name,
                        ToggleType = MenuItemToggleType.Radio, IsChecked = Group == definition.Id,
                    };
                    item.Click += (_, _) => GroupChangeRequested?.Invoke(this, definition.Id);
                    group.Items.Add(item);
                }

                group.Items.Add(new Separator());
                var newGroup = Command("PinnedImageNewGroup", EditGroupAsync, "new-group");
                SetMenuShortcut(newGroup, "new-group");
                group.Items.Add(newGroup);
                group.Items.Add(Command("PinnedImageHideGroup", () =>
                {
                    GroupVisibilityRequested?.Invoke(Group, false);
                    return Task.CompletedTask;
                }));
                group.Items.Add(Command("PinnedImageShowGroup", () =>
                {
                    GroupVisibilityRequested?.Invoke(Group, true);
                    return Task.CompletedTask;
                }));

                foreach (var item in menu.Items.OfType<MenuItem>())
                    if (item.Tag is string action)
                        SetMenuShortcut(item, action);
            };
            return menu;
        }

        private void SetMenuShortcut(MenuItem item, string action)
        {
            item.InputGesture = PreferencesProvider().ShowMenuShortcuts
                ? BuiltinShortcuts.Snapshot().FirstOrDefault(binding =>
                    binding.ContextKey == (action.StartsWith("text-", StringComparison.Ordinal)
                        ? "BuiltinShortcutPinnedTextContext"
                        : "BuiltinShortcutPinnedContext") && binding.Action == action)?.Gesture
                : null;
        }
    }
}

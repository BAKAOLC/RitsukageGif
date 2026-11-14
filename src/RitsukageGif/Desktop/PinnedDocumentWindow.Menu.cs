using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        bool IPinnedWindow.IsLocked => _locked;

        void IPinnedWindow.SetLocked(bool value)
        {
            if (_locked == value) return;
            _locked = value;
            RefreshPreferencePresentation();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

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
            ConfigureFileMenu(menu);
            if (Document is PinnedTextDocument or PinnedHtmlDocument)
            {
                var copySelected = Command("PinnedImageCopySelectedText", () =>
                    _textView is { SelectedText.Length: > 0 } text
                        ? CopySelectedDocumentTextAsync(text)
                        : Task.CompletedTask, "copy-selected-text");
                copySelected.IsVisible = false;
                menu.Items.Add(copySelected);
                var selectable = Command("PinnedTextSelectable", () =>
                {
                    SetSelectable(!Selectable);
                    return Task.CompletedTask;
                });
                selectable.Tag = "text-selection";
                selectable.ToggleType = MenuItemToggleType.CheckBox;
                menu.Items.Add(selectable);
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

                var translate = Command("PinnedInlineTranslate", ToggleInlineTranslationAsync, "translate-inline");
                translate.ToggleType = MenuItemToggleType.CheckBox;
                menu.Items.Add(translate);
                menu.Items.Add(new Separator());
                menu.Opened += (_, _) =>
                {
                    copySelected.IsVisible = _textView is { SelectedText.Length: > 0 };
                    selectable.IsChecked = Selectable;
                    ignore?.SetCurrentValue(MenuItem.IsCheckedProperty, IgnoreFormat);
                    translate.IsVisible = _inlineTranslation?.Cached is not null ||
                                          ScriptTextTranslationService.ConfigurationFailure(
                                              TranslationPreferencesProvider()) is null;
                    translate.IsChecked = _inlineTranslation is { Enabled: true };
                };
            }

            menu.Items.Add(Command("PinnedDocumentCopyImage", CopyRenderedImageAsync, "copy-image"));
            var copyOriginal = Command("PinnedCopyOriginalImage", () => CopyRenderedImageAsync(true),
                "copy-original");
            copyOriginal.IsVisible = false;
            menu.Items.Add(copyOriginal);
            menu.Items.Add(Command("PinnedDocumentSaveImage", SaveRenderedImageAsync, "save"));
            var saveOriginal = Command("PinnedOriginalImageSaveAs", () => SaveRenderedImageAsync(true),
                "save-original");
            saveOriginal.IsVisible = false;
            menu.Items.Add(saveOriginal);
            menu.Items.Add(new Separator());
            var mark = Command("PinnedMenuMark", () => SetMarkEditingAsync(!IsMarkEditing), "annotate");
            mark.ToggleType = MenuItemToggleType.CheckBox;
            menu.Items.Add(mark);
            var hideMark = Command("PinnedMenuHideMark", () =>
            {
                SetMarkHidden(!IsMarkHidden);
                return Task.CompletedTask;
            }, "hide-mark");
            hideMark.ToggleType = MenuItemToggleType.CheckBox;
            menu.Items.Add(hideMark);
            var through = Command("PinnedImageMouseThrough", () =>
            {
                SetMouseThrough(!_mouseThrough);
                return Task.CompletedTask;
            }, "through");
            through.ToggleType = MenuItemToggleType.CheckBox;
            menu.Items.Add(through);
            menu.Items.Add(Presentation.CreateWindowShadeMenu(menu));
            var topmost = Command("PinnedMenuTopmost", () =>
            {
                Topmost = !Topmost;
                RefreshPreferencePresentation();
                return Task.CompletedTask;
            }, "topmost");
            topmost.ToggleType = MenuItemToggleType.CheckBox;
            menu.Items.Add(topmost);
            var hideOthers = Command("PinnedMenuHideOthers", () =>
            {
                Presentation.HideOthersRequested?.Invoke();
                return Task.CompletedTask;
            }, "hide-others");
            hideOthers.ToggleType = MenuItemToggleType.CheckBox;
            menu.Items.Add(hideOthers);
            menu.Items.Add(new Separator());
            var group = new MenuItem { Tag = "move-group" };
            group.Bind(HeaderedSelectingItemsControl.HeaderProperty, LiveText.Resource("PinnedMoveToGroup"));
            menu.Items.Add(group);
            var thumbnail = Command("PinnedMenuThumbnail", () =>
            {
                ToggleDocumentThumbnail(screenAnchor: new(0, 0));
                return Task.CompletedTask;
            }, "thumbnail");
            thumbnail.ToggleType = MenuItemToggleType.CheckBox;
            menu.Items.Add(thumbnail);
            menu.Items.Add(Command("PinnedTitleEdit", () =>
            {
                Presentation.Title.BeginEdit();
                return Task.CompletedTask;
            }, "set-title"));
            var locked = Command("PinnedMenuLock", () =>
            {
                _locked = !_locked;
                RefreshPreferencePresentation();
                StateChanged?.Invoke(this, EventArgs.Empty);
                return Task.CompletedTask;
            }, "lock");
            locked.ToggleType = MenuItemToggleType.CheckBox;
            menu.Items.Add(locked);
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
            menu.Items.Add(new Separator());
            if (Document is not PinnedFilesDocument)
                menu.Items.Add(Command("PinnedImageCopyFile", CopyAsFileAsync, "copy-file"));
            menu.Items.Add(Command("PinnedImageSelectThumbnailRegion", () =>
            {
                SelectDocumentThumbnailRegion();
                return Task.CompletedTask;
            }, "select-roi"));
            menu.Items.Add(Command("PinnedImageMultiSelect", () =>
            {
                SelectionRequested?.Invoke(this);
                return Task.CompletedTask;
            }, "select"));
            menu.Items.Add(Command("PinnedSwitchNext", () =>
            {
                Presentation.SwitchNextRequested?.Invoke();
                return Task.CompletedTask;
            }, "switch-next"));
            menu.Items.Add(Command("PinnedImageHide", () =>
            {
                HideImage();
                return Task.CompletedTask;
            }, "hide"));
            menu.Opened += (_, _) =>
            {
                copyOriginal.IsVisible = saveOriginal.IsVisible =
                    _isThumbnail || DocumentSceneViewScale is not 1 || Opacity < 1 || DisplayedTranslation is not null;
                thumbnail.IsChecked = _isThumbnail;
                thumbnail.IsEnabled = !IsInteractionLocked && !IsMarkEditing;
                mark.IsChecked = IsMarkEditing;
                mark.IsEnabled = !_closed && !_mouseThrough;
                hideMark.IsChecked = IsMarkHidden;
                hideMark.IsEnabled = !IsMarkEditing;
                locked.IsChecked = _locked;
                through.IsChecked = _mouseThrough;
                through.IsEnabled = PinnedWindowPlatform.CanSetMouseThrough(this) &&
                                    (_mouseThrough || _canRestoreHidden());
                topmost.IsChecked = Topmost;
                hideOthers.IsChecked = Presentation.HideOthersCheckedProvider();
                group.Items.Clear();
                foreach (var definition in GroupsProvider())
                {
                    var item = new MenuItem
                    {
                        Header = Presentation.CreateGroupMenuHeader(definition),
                        ToggleType = definition.Color is null && Group == definition.Id
                            ? MenuItemToggleType.CheckBox
                            : MenuItemToggleType.None,
                        IsChecked = definition.Color is null && Group == definition.Id,
                        IsEnabled = Group != definition.Id,
                    };
                    if (definition.Color is not null)
                        item.Icon = PinnedGroupIcon.Create(item, definition, Group == definition.Id);
                    item.Click += (_, _) => GroupChangeRequested?.Invoke(this, definition.Id);
                    group.Items.Add(item);
                }

                group.Items.Add(new Separator());
                var newGroup = Command("PinnedMoveToNewGroup", EditGroupAsync, "new-group");
                SetMenuShortcut(newGroup, "new-group");
                group.Items.Add(newGroup);

                foreach (var item in menu.Items.OfType<MenuItem>())
                    if (item.Tag is string action)
                        SetMenuShortcut(item, action);
            };
            Presentation.AddSourceMimeCommands(menu, PreferencesProvider);
            Presentation.AddConfigurationCommand(menu);
            AddColorCopyCommands(menu);
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

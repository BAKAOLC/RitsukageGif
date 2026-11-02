using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private Grid ActionsPage(AppSettings draft)
        {
            var layout = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new(12), RowSpacing = 8 };
            var background = new Border { Background = AppTheme.SurfaceTint, CornerRadius = new(8) };
            Grid.SetRowSpan(background, 3);
            layout.Children.Add(background);
            var error = new TextBlock
            {
                Foreground = AppTheme.Error, TextWrapping = TextWrapping.Wrap, Margin = new(16, 12, 16, 0),
            };
            layout.Children.Add(error);
            var rows = new StackPanel { Name = "GlobalActionsRows" };
            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalContentAlignment = VerticalAlignment.Top,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border
                {
                    Margin = new(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top,
                    Child = MouseSettingsCard(rows),
                },
            };
            Grid.SetRow(scroll, 1);
            layout.Children.Add(scroll);
            var add = ActionPageButton("GlobalActionAdd");
            var clearActions = ActionPageButton("GlobalActionClearAll");
            var clearShortcuts = ActionPageButton("GlobalActionClearShortcuts");
            var ignored = ActionPageButton("HotkeyIgnoredPrograms");
            var commands = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { add, clearActions, clearShortcuts, ignored },
            };
            var footer = new StackPanel { Spacing = 8, Margin = new(12, 0, 12, 12), Children = { commands } };
            Grid.SetRow(footer, 2);
            layout.Children.Add(footer);
            var statuses = new Dictionary<string, ContentControl>(StringComparer.Ordinal);
            add.Click += async (_, _) =>
            {
                var action = new GlobalActionBinding { Kind = GlobalActionKind.RunScript };
                if (!await EditGlobalActionAsync(action)) return;
                draft.GlobalActions.Add(action);
                Refresh();
                RequestLiveApply();
            };
            clearActions.Click += (_, _) =>
            {
                var identifiers = draft.GlobalActions.Select(action => action.Id).ToHashSet(StringComparer.Ordinal);
                draft.GlobalActions.Clear();
                draft.GlobalMouseBindings.RemoveAll(binding => identifiers.Contains(binding.ActionId));
                Refresh();
                RequestLiveApply();
            };
            clearShortcuts.Click += (_, _) =>
            {
                foreach (var action in draft.GlobalActions) action.Shortcut = "";
                Refresh();
                RequestLiveApply();
            };
            AddHotkeyPolicyControls(draft, footer, ignored);
            layout.AttachedToVisualTree += (_, _) => Refresh();
            SubscribeContentLanguage(Refresh);
            HotkeyRegistrationsChanged += ValidateDraft;
            RegisterContentCleanup(() => HotkeyRegistrationsChanged -= ValidateDraft);
            Refresh();
            return layout;

            void Refresh()
            {
                rows.Children.Clear();
                statuses.Clear();
                foreach (var action in draft.GlobalActions.ToArray())
                {
                    var label = ActionLabel(action);
                    var row = ActionSettingsRow(label);
                    SettingsField.Track(row, row.Children[0],
                        settings => settings.GlobalActions.FirstOrDefault(item => item.Id == action.Id),
                        AppSettings.Same);
                    var editor = HotkeyBox(KeyGestureDisplay.Format(action.Shortcut));
                    editor.Name = "GlobalActionShortcut";
                    var shortcut = ActionHotkeyField(editor, label, out var status);
                    statuses[action.Id] = status;
                    var tray = new CheckBox
                    {
                        Content = UiText.Get("GlobalActionShowInTray"), IsChecked = action.ShowInTray,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    var edit = BindingButton("GlobalActionEdit", "M4,16 L16,4 20,8 8,20 3,21 Z M14,6 L18,10");
                    var remove = BindingButton("SelectionShortcutRemove", "M6,6 L18,18 M18,6 L6,18");
                    remove.Foreground = AppTheme.Error;
                    Control[] controls = [shortcut, tray, edit, remove];
                    for (var index = 0; index < controls.Length; index++)
                    {
                        Grid.SetColumn(controls[index], index + 1);
                        row.Children.Add(controls[index]);
                    }

                    rows.Children.Add(ActionRowBorder(row));
                    editor.TextChanged += (_, _) =>
                    {
                        action.Shortcut = editor.Text ?? "";
                        ValidateDraft();
                    };
                    tray.IsCheckedChanged += (_, _) =>
                    {
                        action.ShowInTray = tray.IsChecked == true;
                        ValidateDraft();
                    };
                    edit.Click += async (_, _) =>
                    {
                        if (await EditGlobalActionAsync(action))
                        {
                            Refresh();
                            RequestLiveApply();
                        }
                    };
                    remove.Click += (_, _) =>
                    {
                        draft.GlobalActions.Remove(action);
                        draft.GlobalMouseBindings.RemoveAll(binding => binding.ActionId == action.Id);
                        Refresh();
                        RequestLiveApply();
                    };
                }

                add.Content = UiText.Get("GlobalActionAdd");
                clearActions.Content = UiText.Get("GlobalActionClearAll");
                clearShortcuts.Content = UiText.Get("GlobalActionClearShortcuts");
                ValidateDraft();
            }

            IEnumerable<string> ReservedKeys()
            {
                return draft.PresetHotkeys.Select(binding => binding.Shortcut);
            }

            void ValidateDraft()
            {
                var errors = GlobalActions.Validate(draft.GlobalActions, ReservedKeys(), draft.GlobalMouseBindings)
                    .ToList();
                try
                {
                    HotkeyService.Validate(ReservedKeys());
                }
                catch (ArgumentException exception)
                {
                    errors.Add(exception.Message);
                }

                error.Text = string.Join(Environment.NewLine, errors.Distinct());
                error.IsVisible = errors.Count > 0;
                foreach (var action in draft.GlobalActions)
                {
                    if (!statuses.TryGetValue(action.Id, out var status)) continue;
                    var reserved = ReservedKeys().Concat(draft.GlobalActions
                        .Where(other => other.Id != action.Id && other.Enabled).Select(other => other.Shortcut));
                    var rowErrors = GlobalActions.Validate([action], reserved, draft.GlobalMouseBindings);
                    SetActionHotkeyStatus(status, action,
                        rowErrors.Count > 0 ? string.Join(Environment.NewLine, rowErrors) : null);
                }
            }
        }

        private static Button ActionPageButton(string key)
        {
            return new() { Content = UiText.Get(key), Padding = new(10, 6), MinHeight = 32 };
        }

        private static Grid ActionSettingsRow(string name)
        {
            var row = new Grid
                { ColumnDefinitions = new("1.2*,1*,Auto,28,28"), ColumnSpacing = 8, MinHeight = 36 };
            row.Children.Add(new TextBlock
            {
                Text = name, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            });
            return row;
        }

        private static Border ActionRowBorder(Control row)
        {
            return new()
            {
                Child = row, Padding = new(0, 2), BorderBrush = AppTheme.Outline,
                BorderThickness = new(0, 0, 0, 1),
            };
        }

        private static Grid ActionHotkeyField(TextBox editor, string label, out ContentControl status)
        {
            editor.MinWidth = 0;
            editor.MinHeight = 32;
            editor.HorizontalAlignment = HorizontalAlignment.Stretch;
            editor.HorizontalContentAlignment = HorizontalAlignment.Center;
            editor.Padding = new(27, 2, 27, 2);
            AutomationProperties.SetName(editor, label);
            var grid = new Grid { ColumnDefinitions = new("26,*,26"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumnSpan(editor, 3);
            grid.Children.Add(editor);
            status = new()
            {
                Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            grid.Children.Add(status);
            var clear = BindingButton("Clear", "M6,6 L18,18 M18,6 L6,18");
            clear.Width = clear.Height = 22;
            clear.Padding = new(4);
            clear.Foreground = AppTheme.Error;
            clear.Background = Brushes.Transparent;
            clear.VerticalAlignment = VerticalAlignment.Center;
            clear.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(clear, 2);
            grid.Children.Add(clear);
            clear.Click += (_, _) => editor.Text = "";

            editor.TextChanged += (_, _) => RefreshClear();
            RefreshClear();
            return grid;

            void RefreshClear()
            {
                clear.IsVisible = !string.IsNullOrWhiteSpace(editor.Text);
            }
        }

        private void SetActionHotkeyStatus(ContentControl status, GlobalActionBinding action, string? error)
        {
            var hasShortcut = !string.IsNullOrWhiteSpace(action.Shortcut);
            var registration = error is null && action.Enabled && hasShortcut
                ? HotkeyRegistration?.Invoke(action)
                : null;
            error ??= registration is { State: HotkeyRegistrationState.Failed } ? registration.Error : null;
            var active = error is null && action.Enabled &&
                         registration?.State == HotkeyRegistrationState.Registered;
            SetMouseBindingStatus(status, error, active);
            var description = !action.Enabled
                ? UiText.Get("GlobalActionDisabledState")
                : error ?? (registration is { State: HotkeyRegistrationState.Disabled, Error: { } reason }
                    ? UiText.Get(reason)
                    : UiText.Get(!hasShortcut ? "MouseBindingNotSet" :
                        active ? "HotkeyRegistered" : "HotkeyRegistrationPending"));
            if (active && registration?.TriggerDescription is { } trigger)
                description += Environment.NewLine + trigger;
            ToolTip.SetTip(status, description);
            AutomationProperties.SetName(status, description);
            status.IsVisible = error is not null || hasShortcut || !action.Enabled;
        }

        private static string ActionLabel(GlobalActionBinding action)
        {
            return GlobalActions.Label(action);
        }
    }
}

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private StackPanel GlobalMousePage(AppSettings draft)
        {
            var page = Page();
            var mask = new CheckBox
            {
                Name = "GlobalMouseShowMask", [!ContentProperty] = LiveText.Resource("GlobalMouseShowMask"),
                IsChecked = draft.GlobalMouseShowMask, IsEnabled = GlobalMouseService.Supported,
            };
            mask.IsCheckedChanged += (_, _) => draft.GlobalMouseShowMask = mask.IsChecked == true;
            page.Children.Add(new TextBlock
            {
                [!TextBlock.TextProperty] = LiveText.Resource("GlobalMouseHint"),
                TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Muted, FontSize = 12,
            });
            page.Children.Add(new TextBlock
            {
                [!TextBlock.TextProperty] = LiveText.Resource("GlobalMouseUnsupported"),
                IsVisible = !GlobalMouseService.Supported, Foreground = AppTheme.Muted,
                TextWrapping = TextWrapping.Wrap, FontSize = 12,
            });
            var rows = new StackPanel();
            var clear = ActionPageButton("GlobalMouseClearAll");
            clear.Name = "ClearGlobalMouseBindings";
            clear.Bind(ContentProperty, LiveText.Resource("GlobalMouseClearAll"));
            var ignored = ActionPageButton("HotkeyIgnoredPrograms");
            ignored.Name = "GlobalMouseIgnoredPrograms";
            ignored.HorizontalAlignment = HorizontalAlignment.Left;
            ignored.IsEnabled = GlobalMouseService.Supported && PortableForegroundPolicy.Supported;
            if (!PortableForegroundPolicy.Supported)
                ignored.Bind(ToolTip.TipProperty, LiveText.Resource("HotkeyPolicyUnavailable"));
            ignored.Click += async (_, _) =>
            {
                var programs = await EditIgnoredProgramsAsync(draft.GlobalMouseIgnoredPrograms);
                if (programs is null) return;
                draft.GlobalMouseIgnoredPrograms = programs;
                RefreshIgnored();
                RequestLiveApply();
            };
            SettingsField.Track(ignored, ignored, static settings => settings.GlobalMouseIgnoredPrograms.Count);
            var maskRow = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(0, 6) };
            maskRow.Children.Add(Setting(mask, static settings => settings.GlobalMouseShowMask));
            Grid.SetColumn(clear, 1);
            maskRow.Children.Add(clear);
            page.Children.Add(MouseSettingsCard(new StackPanel
            {
                Children = { rows, maskRow, ignored },
            }));
            var statuses = new Dictionary<string, ContentControl>(StringComparer.Ordinal);
            clear.Click += (_, _) =>
            {
                draft.GlobalMouseBindings.Clear();
                draft.GlobalActions.RemoveAll(GlobalMouseActionCatalog.IsMouseOnly);
                Refresh();
                RequestLiveApply();
            };
            page.AttachedToVisualTree += (_, _) => Refresh();
            SubscribeContentLanguage(Refresh);
            HotkeyRegistrationsChanged += Validate;
            RegisterContentCleanup(() => HotkeyRegistrationsChanged -= Validate);
            Refresh();
            return page;

            void RefreshIgnored()
            {
                ignored.Content = string.Format(UiText.Get("HotkeyIgnoredPrograms"),
                    draft.GlobalMouseIgnoredPrograms.Count);
            }

            void Refresh()
            {
                rows.Children.Clear();
                statuses.Clear();
                foreach (var (target, fixedRow) in Targets())
                {
                    var binding = draft.GlobalMouseBindings.FirstOrDefault(item => item.ActionId == target.Id);
                    _ = InputMouseGesture.TryParse(binding?.Gesture, out var gesture);
                    var modifiers = new ModifierKeysPicker(gesture.Modifiers);
                    var kinds = new[] { MouseGestureKind.None }.Concat(Enum.GetValues<MouseGestureKind>().Where(kind =>
                        kind != MouseGestureKind.None && ((int)kind & ((int)kind - 1)) == 0 &&
                        (MouseGestureKind.Drags & kind) != 0)).ToArray();
                    var kindBox = new ComboBox
                    {
                        ItemsSource = kinds.Select(kind => UiText.Get(kind == MouseGestureKind.None
                            ? "MouseGestureNone"
                            : "MouseGesture" + kind)).ToArray(),
                        SelectedIndex = binding is null ? 0 : Array.IndexOf(kinds, gesture.Kind),
                        PlaceholderText = binding?.Gesture,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                    };
                    var status = new ContentControl { Width = 24, VerticalAlignment = VerticalAlignment.Center };
                    statuses[target.Id] = status;
                    var label = fixedRow
                        ? GlobalMouseActionCatalog.FixedLabel(target.Kind)
                        : ActionLabel(target);
                    var row = MouseSettingsRow(label, modifiers, kindBox, status);
                    SettingsField.Track(row, ((Grid)row.Child!).Children[0],
                        settings => settings.GlobalMouseBindings.FirstOrDefault(item => item.ActionId == target.Id));
                    row.IsEnabled = GlobalMouseService.Supported;
                    rows.Children.Add(row);
                    modifiers.ValueChanged += Store;
                    kindBox.SelectionChanged += (_, _) => Store();
                    continue;

                    void Store()
                    {
                        if (kindBox.SelectedIndex < 0) return;
                        var kind = kinds[kindBox.SelectedIndex];
                        draft.GlobalMouseBindings.RemoveAll(item => item.ActionId == target.Id);
                        if (kind != MouseGestureKind.None)
                        {
                            if (draft.GlobalActions.All(item => item.Id != target.Id))
                            {
                                target.Enabled = true;
                                draft.GlobalActions.Add(target);
                            }

                            draft.GlobalMouseBindings.Add(new()
                            {
                                ActionId = target.Id,
                                Gesture = new InputMouseGesture(kind, modifiers.Value).ToString(), Enabled = true,
                            });
                        }

                        if (GlobalMouseActionCatalog.IsMouseOnly(target) &&
                            draft.GlobalMouseBindings.All(item => item.ActionId != target.Id))
                            draft.GlobalActions.Remove(target);
                        clear.IsEnabled = draft.GlobalMouseBindings.Count != 0;
                        Validate();
                        RequestLiveApply();
                    }
                }

                clear.IsEnabled = GlobalMouseService.Supported && draft.GlobalMouseBindings.Count != 0;
                RefreshIgnored();
                Validate();
            }

            (GlobalActionBinding Action, bool FixedRow)[] Targets()
            {
                var result = new List<(GlobalActionBinding Action, bool FixedRow)>();
                var included = new HashSet<string>(StringComparer.Ordinal);
                foreach (var kind in GlobalMouseActionCatalog.FixedKinds)
                {
                    var id = GlobalMouseActionCatalog.FixedId(kind);
                    var standardId = AppSettings.DefaultActionId(kind);
                    var action = draft.GlobalActions.FirstOrDefault(item => item.Id == id && item.Kind == kind &&
                                                                            draft.GlobalMouseBindings.Any(binding =>
                                                                                binding.ActionId == id)) ??
                                 draft.GlobalActions.FirstOrDefault(item =>
                                     item.Id == standardId && item.Kind == kind &&
                                     string.IsNullOrEmpty(item.Parameter) &&
                                     item.Arguments.Count == 0) ??
                                 draft.GlobalActions.FirstOrDefault(item => item.Id == id && item.Kind == kind) ??
                                 new GlobalActionBinding
                                 {
                                     Id = id, Kind = kind, Enabled = false, ShowInTray = false,
                                 };
                    result.Add((action, true));
                    included.Add(action.Id);
                }

                foreach (var binding in draft.GlobalMouseBindings)
                {
                    if (included.Contains(binding.ActionId)) continue;
                    var action = draft.GlobalActions.FirstOrDefault(item => item.Id == binding.ActionId);
                    if (action is null) continue;
                    included.Add(action.Id);
                    result.Add((action, false));
                }

                return [.. result];
            }

            void Validate()
            {
                foreach (var (id, status) in statuses)
                {
                    var bindings = draft.GlobalMouseBindings.Where(item => item.ActionId == id).ToArray();
                    var errors = GlobalMouseService.Validate(bindings, draft.GlobalActions);
                    var error = errors.Count == 0 ? null : errors[0];
                    foreach (var binding in bindings.Where(item => item.Enabled))
                    {
                        if (!InputMouseGesture.TryParse(binding.Gesture, out var gesture)) continue;
                        if ((gesture.Kind & MouseGestureKind.Drags) == 0)
                            error ??= UiText.Get("GlobalMouseInvalidBinding");
                        if (draft.GlobalMouseBindings.Any(other => other.ActionId != id && other.Enabled &&
                                                                   InputMouseGesture.TryParse(other.Gesture,
                                                                       out var otherGesture) &&
                                                                   otherGesture == gesture))
                            error ??= UiText.Get("GlobalMouseConflict");
                    }

                    var action = draft.GlobalActions.FirstOrDefault(item => item.Id == id);
                    var activeBinding = bindings.FirstOrDefault(item => item.Enabled);
                    var configured = action is { Enabled: true } && activeBinding is not null;
                    var registration = configured ? GlobalMouseRegistration?.Invoke(id, activeBinding!.Gesture) : null;
                    error ??= registration is { State: HotkeyRegistrationState.Failed } ? registration.Error : null;
                    SetMouseBindingStatus(status, error,
                        configured && registration?.State == HotkeyRegistrationState.Registered);
                    var description = action is { Enabled: false }
                        ? UiText.Get("GlobalActionDisabledState")
                        : error ?? (registration is { State: HotkeyRegistrationState.Disabled, Error: { } reason }
                            ? UiText.Get(reason)
                            : UiText.Get(!configured
                                ? "MouseBindingNotSet"
                                : registration?.State == HotkeyRegistrationState.Registered
                                    ? "MouseBindingReady"
                                    : "HotkeyRegistrationPending"));
                    ToolTip.SetTip(status, description);
                    AutomationProperties.SetName(status, description);
                }
            }
        }
    }
}

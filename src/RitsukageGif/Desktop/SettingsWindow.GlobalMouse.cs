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
            page.Children.Add(MouseSettingsCard(rows));
            var statuses = new Dictionary<string, ContentControl>(StringComparer.Ordinal);
            page.AttachedToVisualTree += (_, _) => Refresh();
            SubscribeContentLanguage(Refresh);
            Refresh();
            return page;

            void Refresh()
            {
                rows.Children.Clear();
                statuses.Clear();
                foreach (var target in Targets())
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
                    var row = MouseSettingsRow(target.Id.StartsWith("builtin.mouse.", StringComparison.Ordinal)
                        ? UiText.Get("GlobalAction" + target.Kind)
                        : ActionLabel(target), modifiers, kindBox, status);
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

                        if (target.Id.StartsWith("builtin.mouse.", StringComparison.Ordinal) &&
                            !target.ShowInTray && string.IsNullOrWhiteSpace(target.Shortcut) &&
                            draft.GlobalMouseBindings.All(item => item.ActionId != target.Id))
                            draft.GlobalActions.Remove(target);
                        Validate();
                        RequestLiveApply();
                    }
                }

                Validate();
            }

            GlobalActionBinding[] Targets()
            {
                var kinds = new[]
                {
                    GlobalActionKind.ScreenshotAndPin, GlobalActionKind.ScreenshotAndCopy,
                    GlobalActionKind.ScreenshotAndCopyText, GlobalActionKind.ScreenshotQuickSave,
                    GlobalActionKind.Screenshot, GlobalActionKind.ScreenshotAndScroll,
                    GlobalActionKind.StartRecording,
                };
                var result = new List<GlobalActionBinding>();
                var included = new HashSet<string>(StringComparer.Ordinal);
                foreach (var kind in kinds)
                {
                    var id = "builtin.mouse." + kind;
                    var action = draft.GlobalActions.FirstOrDefault(item => item.Id == id && item.Kind == kind &&
                                                                            draft.GlobalMouseBindings.Any(binding =>
                                                                                binding.ActionId == id)) ??
                                 draft.GlobalActions.FirstOrDefault(item => item.Kind == kind &&
                                                                            string.IsNullOrEmpty(item.Parameter) &&
                                                                            item.Arguments.Count == 0) ??
                                 new GlobalActionBinding
                                 {
                                     Id = id, Kind = kind, Enabled = false, ShowInTray = false,
                                 };
                    result.Add(action);
                    included.Add(action.Id);
                }

                foreach (var binding in draft.GlobalMouseBindings)
                {
                    if (included.Contains(binding.ActionId)) continue;
                    var action = draft.GlobalActions.FirstOrDefault(item => item.Id == binding.ActionId);
                    if (action is null) continue;
                    included.Add(action.Id);
                    result.Add(action);
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
                    SetMouseBindingStatus(status, error,
                        action is { Enabled: true } && bindings.Any(item => item.Enabled));
                    if (error is null && action is { Enabled: false })
                    {
                        var description = UiText.Get("GlobalActionDisabledState");
                        ToolTip.SetTip(status, description);
                        AutomationProperties.SetName(status, description);
                    }
                }
            }
        }
    }
}

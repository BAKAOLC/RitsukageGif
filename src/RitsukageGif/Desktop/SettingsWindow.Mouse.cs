using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private StackPanel MouseShortcutsPage(AppSettings draft)
        {
            var page = Page();
            page.Children.Add(SectionHeading("MouseContextPinned"));
            var rows = new StackPanel();
            page.Children.Add(MouseSettingsCard(rows));
            var statuses = new Dictionary<string, Action<string?, bool>>(StringComparer.Ordinal);
            Refresh();
            SubscribeContentLanguage(Refresh);
            return page;

            void Refresh()
            {
                rows.Children.Clear();
                statuses.Clear();
                foreach (var definition in MouseCommands.Catalog.Where(item =>
                             item.Context == MouseGestureContext.Pinned))
                {
                    var gestures = draft.MouseShortcuts.TryGetValue(definition.Id, out var configured)
                        ? configured
                        : definition.DefaultGestures;
                    _ = InputMouseGesture.TryParse(gestures.Count == 0 ? null : gestures[0], out var gesture);
                    var modifiers = new ModifierKeysPicker(gesture.Modifiers);
                    var kinds = new[] { MouseGestureKind.None }.Concat(Enum.GetValues<MouseGestureKind>().Where(kind =>
                        kind != MouseGestureKind.None && ((int)kind & ((int)kind - 1)) == 0 &&
                        (definition.AllowedKinds & kind) != 0)).ToArray();
                    var kindBox = new ComboBox
                    {
                        ItemsSource = kinds.Select(kind => UiText.Get(kind == MouseGestureKind.None
                            ? "MouseGestureNone"
                            : "MouseGesture" + kind)).ToArray(),
                        SelectedIndex = gestures.Count == 0 ? 0 : Array.IndexOf(kinds, gesture.Kind),
                        PlaceholderText = gestures.Count == 0 ? null : gestures[0],
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                    };
                    var status = new ContentControl { Width = 24, VerticalAlignment = VerticalAlignment.Center };
                    rows.Children.Add(MouseSettingsRow(UiText.Get(definition.DescriptionKey), modifiers, kindBox,
                        status));
                    statuses[definition.Id] = (error, active) => SetMouseBindingStatus(status, error, active);
                    modifiers.ValueChanged += Store;
                    kindBox.SelectionChanged += (_, _) => Store();
                    continue;

                    void Store()
                    {
                        if (kindBox.SelectedIndex < 0) return;
                        var kind = kinds[kindBox.SelectedIndex];
                        draft.MouseShortcuts[definition.Id] = kind == MouseGestureKind.None
                            ? []
                            : [new InputMouseGesture(kind, modifiers.Value).ToString()];
                        Validate();
                        RequestLiveApply();
                    }
                }

                Validate();
            }

            void Validate()
            {
                var issues = MouseCommands.ValidateIssues(draft.MouseShortcuts);
                foreach (var definition in MouseCommands.Catalog.Where(item => statuses.ContainsKey(item.Id)))
                {
                    var issue = issues.FirstOrDefault(item =>
                        item.Id == definition.Id || item.ConflictingId == definition.Id);
                    var active = draft.MouseShortcuts.TryGetValue(definition.Id, out var values)
                        ? values.Length > 0
                        : definition.DefaultGestures.Count > 0;
                    statuses[definition.Id](issue?.Message, active);
                }
            }
        }

        private static Border MouseSettingsCard(Control content)
        {
            return new()
            {
                Background = AppTheme.SurfaceTint, CornerRadius = new(8), Padding = new(16, 4), Child = content,
            };
        }

        private static Border MouseSettingsRow(string label, ModifierKeysPicker modifiers, ComboBox gesture,
            ContentControl status)
        {
            var row = new Grid { ColumnDefinitions = new("1.35*,1*,18,1.15*,24"), ColumnSpacing = 10 };
            row.Children.Add(new TextBlock
            {
                Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(modifiers, 1);
            row.Children.Add(modifiers);
            var plus = new TextBlock
            {
                Text = "+", HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(plus, 2);
            row.Children.Add(plus);
            Grid.SetColumn(gesture, 3);
            row.Children.Add(gesture);
            Grid.SetColumn(status, 4);
            row.Children.Add(status);
            return new()
            {
                Child = row, Padding = new(0, 6), BorderBrush = AppTheme.Outline, BorderThickness = new(0, 0, 0, 1),
            };
        }

        private static void SetMouseBindingStatus(ContentControl control, string? error, bool active)
        {
            var description = error ?? UiText.Get(active ? "MouseBindingReady" : "MouseBindingNotSet");
            control.Content = new Path
            {
                Data = Geometry.Parse(error is not null ? "M5,5 L19,19 M19,5 L5,19" :
                    active ? "M4,12 L10,18 21,5" : "M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 M6,12 L18,12"),
                Width = 18, Height = 18, StrokeThickness = 2, Stretch = Stretch.Uniform,
                Stroke = error is not null ? AppTheme.Error : active ? AppTheme.Success : AppTheme.Muted,
            };
            ToolTip.SetTip(control, description);
            AutomationProperties.SetName(control, description);
        }
    }
}

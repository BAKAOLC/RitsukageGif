using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private Grid SelectionShortcutsPage(AppSettings draft)
        {
            var layout = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new(16), RowSpacing = 12 };
            var diagnostics = new TextBlock { Foreground = AppTheme.Error, TextWrapping = TextWrapping.Wrap };
            layout.Children.Add(diagnostics);
            var tabs = new TabControl();
            Grid.SetRow(tabs, 1);
            layout.Children.Add(tabs);
            var reset = new Button { HorizontalAlignment = HorizontalAlignment.Right };
            reset.Bind(ContentProperty, LiveText.Resource("BuiltinShortcutResetAll"));
            Grid.SetRow(reset, 2);
            layout.Children.Add(reset);
            var statuses = new List<(BuiltinShortcutDefinition Definition, ContentControl Status)>();
            reset.Click += (_, _) =>
            {
                draft.SelectionShortcuts.Clear();
                Refresh();
                RequestLiveApply();
            };
            SubscribeContentLanguage(Refresh);
            Refresh();
            return layout;

            void Refresh()
            {
                var selected = Math.Max(0, tabs.SelectedIndex);
                statuses.Clear();
                var contexts = new[]
                {
                    "BuiltinShortcutSelectionContext", "BuiltinShortcutScreenshotContext",
                    "BuiltinShortcutPinnedContext", "BuiltinShortcutLongshotContext",
                };
                tabs.ItemsSource = contexts.Select(context =>
                {
                    var rows = new StackPanel();
                    foreach (var definition in BuiltinShortcuts.Registrations()
                                 .Where(item => item.ContextKey == context))
                        AddRegistration(rows, definition);
                    return ContextTab(context, rows);
                }).ToArray();
                tabs.SelectedIndex = selected;
                Validate();
            }

            TabItem ContextTab(string title, StackPanel rows)
            {
                return new()
                {
                    Header = UiText.Get(title),
                    Content = new ScrollViewer
                    {
                        Margin = new(0, 12, 0, 0),
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                        Content = new Border { Margin = new(0, 0, 12, 0), Child = MouseSettingsCard(rows) },
                    },
                };
            }

            void Validate()
            {
                var issues = SelectionCommands.ValidateIssues(draft.SelectionShortcuts);
                diagnostics.Text = string.Join(Environment.NewLine, issues.Select(issue => issue.Message));
                diagnostics.IsVisible = issues.Count > 0;
                foreach (var (definition, status) in statuses)
                {
                    var issue = issues.FirstOrDefault(item =>
                        item.Id == definition.Id || item.ConflictingId == definition.Id ||
                        (definition.SelectionId is { } selection &&
                         (item.Id == selection || item.ConflictingId == selection)));
                    SetMouseBindingStatus(status, issue?.Message,
                        !string.IsNullOrWhiteSpace(BuiltinShortcuts.GetGesture(definition, draft.SelectionShortcuts)));
                }
            }

            void AddRegistration(StackPanel rows, BuiltinShortcutDefinition definition)
            {
                var name = UiText.Get(definition.DescriptionKey);
                if (definition.DirectionKey != Key.None)
                    name = UiText.Format("BuiltinShortcutDirectionalOperation",
                        UiText.Get("SelectionShortcut" + definition.DirectionKey), name);
                var label = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
                label.Children.Add(new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap });
                var status = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
                statuses.Add((definition, status));

                var editor = new TextBox
                {
                    Text = KeyGestureDisplay.Format(BuiltinShortcuts.GetGesture(definition, draft.SelectionShortcuts)),
                    IsReadOnly = true,
                    Watermark = UiText.Get("SelectionShortcutDisabled"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                };
                AutomationProperties.SetName(editor, name);
                GlobalInputCapture.Observe(editor);
                editor.Bind(ToolTip.TipProperty, LiveText.Resource("EnterHotkey"));
                var clear = BindingButton("Clear", "M6,6 L18,18 M18,6 L6,18");
                var row = new Grid { ColumnDefinitions = new("*,210,28,24"), ColumnSpacing = 12, MinHeight = 36 };
                row.Children.Add(label);
                Grid.SetColumn(editor, 1);
                row.Children.Add(editor);
                Grid.SetColumn(clear, 2);
                row.Children.Add(clear);
                Grid.SetColumn(status, 3);
                row.Children.Add(status);
                rows.Children.Add(new Border
                {
                    Child = row, Padding = new(0, 4), BorderBrush = AppTheme.Outline,
                    BorderThickness = new(0, 0, 0, 1),
                });
                var shiftPending = false;
                var shiftHeld = false;
                editor.LostFocus += (_, _) =>
                {
                    shiftPending = false;
                    shiftHeld = false;
                };
                editor.AddHandler(KeyUpEvent, (_, args) =>
                {
                    if (args.Key is not (Key.LeftShift or Key.RightShift)) return;
                    args.Handled = true;
                    shiftHeld = false;
                    if (shiftPending && args.KeyModifiers == KeyModifiers.None)
                    {
                        editor.Text = "Shift";
                        Store("Shift");
                    }

                    shiftPending = false;
                }, RoutingStrategies.Tunnel);
                editor.AddHandler(KeyDownEvent, (_, args) =>
                {
                    args.Handled = true;
                    if (args.Key is Key.LeftShift or Key.RightShift)
                    {
                        if (!shiftHeld)
                            shiftPending = definition.SelectionId == "cyclecolorformat" &&
                                           args.KeyModifiers == KeyModifiers.Shift;
                        shiftHeld = true;
                        return;
                    }

                    shiftPending = false;
                    if (args.Key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
                    var gesture = new KeyGesture(args.Key, args.KeyModifiers).ToString();
                    editor.Text = KeyGestureDisplay.Format(gesture);
                    Store(gesture);
                }, RoutingStrategies.Tunnel);
                clear.Click += (_, _) =>
                {
                    editor.Text = "";
                    Store("");
                };
                return;

                void Store(string gesture)
                {
                    draft.SelectionShortcuts[definition.Id] = gesture.Length == 0 ? [] : [gesture];
                    RequestLiveApply();
                    Validate();
                }
            }
        }
    }
}

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class AnnotationToolsStrip : StackPanel, IDisposable
    {
        private readonly Action? _adjustTools;
        private readonly bool _recording;

        private readonly Dictionary<Button, (ScreenshotTool Tool, Control Icon, TextBlock? Label,
            TextBlock? Shortcut, TextBlock Title, TextBlock TipShortcut)> _contents = [];
        private readonly List<(Button Button, Button? Drop, IReadOnlyList<ScreenshotTool> Tools)> _groups = [];

        private readonly Dictionary<ScreenshotTool, Button> _groupButtons = [];
        private readonly Dictionary<ScreenshotTool, Button> _menuButtons = [];
        private readonly List<Flyout> _menus = [];
        private readonly Dictionary<Button, ScreenshotTool> _remembered = [];
        private readonly ScreenshotAnnotationSession _session;
        private IReadOnlyList<SelectionCommandDefinition> _shortcuts = [];

        internal AnnotationToolsStrip(ScreenshotAnnotationSession session, Action? adjustTools = null,
            bool recording = false)
        {
            _session = session;
            _adjustTools = adjustTools;
            _recording = recording;
            Orientation = Orientation.Horizontal;
            Spacing = 2;
            Rebuild(ScreenshotToolCatalog.DefaultGroups);
            _session.Changed += Refresh;
            UiText.LanguageChanged += Refresh;
        }

        internal IReadOnlyDictionary<ScreenshotTool, Button> GroupButtons => _groupButtons;

        public void Dispose()
        {
            HideFlyouts();
            _session.Changed -= Refresh;
            UiText.LanguageChanged -= Refresh;
        }

        internal void Rebuild(IReadOnlyList<IReadOnlyList<ScreenshotTool>> groups)
        {
            HideFlyouts();
            _menus.Clear();
            _groups.Clear();
            _groupButtons.Clear();
            _menuButtons.Clear();
            _remembered.Clear();
            _contents.Clear();
            Children.Clear();
            foreach (var tools in groups)
            {
                if (tools.Count == 0) continue;
                var button = CreateButton(tools[0]);
                _remembered.Add(button, tools[0]);
                button.Click += (_, _) => Select(
                    ScreenshotToolCatalog.CanonicalTool(_session.Tool) == _remembered[button]
                        ? ScreenshotTool.Select
                        : _remembered[button]);
                foreach (var tool in tools) _groupButtons.Add(tool, button);
                foreach (var tool in tools)
                {
                    var item = CreateButton(tool);
                    item.Width = double.NaN;
                    item.HorizontalAlignment = HorizontalAlignment.Stretch;
                    item.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    item.Click += (_, _) => Select(tool);
                    _menuButtons.Add(tool, item);
                }

                Button? drop = null;
                var group = new StackPanel { Orientation = Orientation.Horizontal, Children = { button } };
                if (tools.Count > 1)
                {
                    var choices = new StackPanel { Spacing = 1 };
                    foreach (var tool in tools)
                        choices.Children.Add(_menuButtons[tool]);

                    if (_adjustTools is not null)
                    {
                        var adjust = new Button
                        {
                            Focusable = false, HorizontalAlignment = HorizontalAlignment.Right,
                            Background = Brushes.Transparent, BorderThickness = new(0), Foreground = AppTheme.Muted,
                        };
                        adjust.Classes.Add("tool");
                        var caption = new TextBlock
                            { VerticalAlignment = VerticalAlignment.Center, Foreground = AppTheme.Ink };
                        caption.Bind(TextBlock.TextProperty, LiveText.Resource("ToolbarAdjust"));
                        adjust.Bind(AutomationProperties.NameProperty, LiveText.Resource("ToolbarAdjust"));
                        adjust.Content = new StackPanel
                        {
                            Orientation = Orientation.Horizontal, Spacing = 4,
                            Children = { ToolbarIcon.Create(adjust, "ToolbarAdjust"), caption },
                        };
                        adjust.PointerEntered += (_, _) => adjust.Foreground = AppTheme.Accent;
                        adjust.PointerExited += (_, _) => adjust.Foreground = AppTheme.Muted;
                        adjust.Click += (_, _) =>
                        {
                            HideFlyouts();
                            _adjustTools();
                        };
                        choices.Children.Add(adjust);
                    }

                    var menu = new Flyout
                    {
                        OverlayDismissEventPassThrough = true,
                        Content = new ThemeVariantScope { Child = choices },
                    };
                    _menus.Add(menu);
                    drop = new()
                    {
                        Width = 18, MinWidth = 0, MinHeight = 0, Padding = new(2),
                        Focusable = false, Background = Brushes.Transparent, BorderThickness = new(0),
                    };
                    drop.Content = DropDownChevron.Create(drop);
                    drop.Click += (_, _) => ShowChoices();
                    group.Children.Add(drop);

                    group.PointerPressed += (_, args) =>
                    {
                        if (!args.GetCurrentPoint(group).Properties.IsRightButtonPressed) return;
                        ShowChoices();
                        args.Handled = true;
                    };

                    void ShowChoices()
                    {
                        if (_session.IsBusy || !_session.CanAnnotate) return;
                        var open = menu.IsOpen;
                        HideFlyouts();
                        if (!open) menu.ShowAt(button);
                    }
                }

                group.PointerWheelChanged += (_, args) =>
                {
                    if (_session.IsBusy || !_session.CanAnnotate || args.Delta.Y == 0 || tools.Count < 2) return;
                    var index = tools.ToList().IndexOf(_remembered[button]);
                    var next = tools[(index + (args.Delta.Y > 0 ? tools.Count - 1 : 1)) % tools.Count];
                    if (!_menuButtons[next].IsEnabled)
                    {
                        args.Handled = true;
                        return;
                    }

                    var active = _groupButtons.GetValueOrDefault(ScreenshotToolCatalog.CanonicalTool(_session.Tool)) ==
                                 button;
                    _remembered[button] = next;
                    if (active) Select(next);
                    else Refresh();
                    args.Handled = true;
                };
                _groups.Add((button, drop, tools));
                Children.Add(group);
            }

            Refresh();
        }

        internal void RefreshShortcuts(IReadOnlyList<SelectionCommandDefinition> shortcuts)
        {
            _shortcuts = shortcuts;
            Refresh();
        }

        internal void Refresh()
        {
            var current = ScreenshotToolCatalog.CanonicalTool(_session.Tool);
            if (_groupButtons.TryGetValue(current, out var anchor)) _remembered[anchor] = current;
            foreach (var (button, drop, _) in _groups)
            {
                var tool = _remembered[button];
                UpdateButton(button, tool, button == anchor, false);
                if (drop is not null)
                {
                    drop.IsEnabled = _session is { IsBusy: false, CanAnnotate: true };
                    drop.Height = button.Height;
                    drop.SetValue(AutomationProperties.NameProperty,
                        UiText.Get(ScreenshotToolCatalog.Get(tool).NameKey));
                }
            }

            foreach (var (tool, button) in _menuButtons) UpdateButton(button, tool, current == tool, true);
        }

        private static Button CreateButton(ScreenshotTool tool)
        {
            var button = new Button
            {
                Tag = tool, MinWidth = 0, MinHeight = 0,
                Padding = new(5), Margin = new(0), Focusable = false, IsTabStop = false,
                Background = Brushes.Transparent, BorderThickness = new(0), Foreground = AppTheme.Ink,
            };
            button.Classes.Add("tool");
            return button;
        }

        private void UpdateButton(Button button, ScreenshotTool tool, bool active, bool menu)
        {
            var definition = ScreenshotToolCatalog.Get(tool);
            var name = UiText.Get(definition.NameKey);
            var gesture = _shortcuts.FirstOrDefault(binding =>
                binding.Command == SelectionCommand.SelectTool && binding.Tool == tool)?.Gesture;
            if (!_contents.TryGetValue(button, out var controls) || controls.Tool != tool)
            {
                var icon = ToolbarIcon.Create(button, ToolbarIcon.FindDefinition(definition.IconKey) ??
                                                      ToolbarIcon.GetDefinition("AnimationSelection"));
                TextBlock? label = null;
                TextBlock? shortcut = null;
                if (menu)
                {
                    var content = new Grid { ColumnDefinitions = new("18,*,Auto"), ColumnSpacing = 8 };
                    content.Children.Add(icon);
                    label = new() { VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(label, 1);
                    content.Children.Add(label);
                    shortcut = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
                    Grid.SetColumn(shortcut, 2);
                    content.Children.Add(shortcut);
                    button.Content = content;
                }
                else
                {
                    button.Content = icon;
                }

                var title = new TextBlock
                    { TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                var tipShortcut = new TextBlock
                    { TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                ToolTip.SetTip(button, new StackPanel { Spacing = 6, Children = { title, tipShortcut } });
                controls = (tool, icon, label, shortcut, title, tipShortcut);
                _contents[button] = controls;
            }

            var extent = AppTheme.CurrentAppearance.ToolbarIconSize switch
            {
                ToolbarIconSize.Small => 24, ToolbarIconSize.Large => 36, _ => 28,
            };
            button.Height = extent;
            if (!menu) button.Width = extent;
            controls.Icon.Width = controls.Icon.Height = Math.Max(12, extent - 12);
            controls.Label?.Text = name;
            controls.Shortcut?.Text = gesture is null ? null : KeyGestureDisplay.Format(gesture);
            controls.Title.Text = name;
            controls.TipShortcut.Text = gesture is null ? null : KeyGestureDisplay.Format(gesture);
            controls.TipShortcut.IsVisible = gesture is not null;
            button.Tag = definition.NameKey;
            button.SetValue(AutomationProperties.NameProperty, name);
            button.IsEnabled =
                (!_recording || ScreenshotToolCatalog.Get(ScreenshotToolCatalog.CanonicalTool(tool))
                    .SupportsRecording) && (tool == ScreenshotTool.AutoMosaic
                    ? _session.CanDetectPrivacy && _session is { IsBusy: false, CanAnnotate: true }
                    : _session is { IsBusy: false, CanAnnotate: true });
            button.Background = Brushes.Transparent;
            button.Foreground = active && !menu ? AppTheme.Accent : AppTheme.Ink;
            button.BorderBrush = active && menu ? AppTheme.Outline : Brushes.Transparent;
            button.BorderThickness = new(active && menu ? 1 : 0);
            button.Classes.Set("active-tool", active && !menu);
        }

        private void Select(ScreenshotTool tool)
        {
            if (_recording &&
                !ScreenshotToolCatalog.Get(ScreenshotToolCatalog.CanonicalTool(tool)).SupportsRecording) return;
            HideFlyouts();
            _session.CancelPrivacyDetection();
            _session.SelectDrawingTool(tool);
        }

        internal void HideFlyouts()
        {
            foreach (var menu in _menus) menu.Hide();
        }
    }
}

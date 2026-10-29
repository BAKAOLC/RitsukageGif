using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotToolbar
    {
        private readonly Dictionary<Button, ScreenshotTool> _groupTools = [];
        private readonly Dictionary<ScreenshotTool, Button> _toolAnchors = [];
        private readonly List<Panel> _toolGroupChoices = [];
        private readonly List<(Button Button, bool HasSelect)> _toolGroupDrops = [];
        private readonly List<Flyout> _toolGroupFlyouts = [];
        private readonly string _toolMenuGroupName = Guid.NewGuid().ToString("N");

        private void InitializeAdditionalTools()
        {
            foreach (var (tool, nameKey, _, _) in ScreenshotToolCatalog.All)
            {
                var button = new RadioButton
                {
                    Tag = nameKey, GroupName = _toolMenuGroupName, MinHeight = 30,
                    Padding = new(2), Margin = new(0), Foreground = AppTheme.Ink,
                    Focusable = false, IsTabStop = false,
                    Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };
                button.Click += (_, _) =>
                {
                    _moreCommands.Hide();
                    SelectTool(tool);
                };
                button.Content = MenuContent(button, nameKey, ToolGeometry(tool),
                    _shortcutBindings.FirstOrDefault(binding =>
                        binding.Command == SelectionCommand.SelectTool && binding.Tool == tool)?.Gesture);
                button.Bind(AutomationProperties.NameProperty, LiveText.Resource(nameKey));
                _tools.Add(tool, button);
            }
        }

        private static string ToolKey(ScreenshotTool tool)
        {
            return ScreenshotToolCatalog.Get(tool).NameKey;
        }

        private void RebuildToolColumns(ToolbarPreferences preferences)
        {
            HideToolGroups();
            foreach (var choices in _toolGroupChoices) choices.Children.Clear();
            _toolGroupChoices.Clear();
            _toolGroupFlyouts.Clear();
            _toolGroupDrops.Clear();
            _groupTools.Clear();
            _toolAnchors.Clear();
            _toolStrip.Children.Clear();
            _moreTools.Children.Clear();
            var placed = new HashSet<ScreenshotTool>();
            var privacyPlaced = false;
            foreach (var column in preferences.Columns)
            {
                if (column.Count == 0) continue;
                var includePrivacy = column.Contains(ScreenshotTool.Mosaic) && _privacyDetect is not null;
                AddToolGroup(column, includePrivacy);
                privacyPlaced |= includePrivacy;
                foreach (var tool in column) placed.Add(tool);
            }

            foreach (var definition in ScreenshotToolCatalog.All)
                if (!placed.Contains(definition.Tool))
                    _moreTools.Children.Add(_tools[definition.Tool]);
            if (!privacyPlaced && _privacyDetect is not null) _moreTools.Children.Add(_privacyDetect);
        }

        private void AddToolGroup(List<ScreenshotTool> variants, bool includePrivacy)
        {
            var initial = variants[0];
            var button = SmallButton(ToolKey(initial), () => { }, ToolGeometry(initial));
            button.Click += (_, _) => SelectTool(_groupTools[button]);
            _groupTools.Add(button, initial);
            SetToolButtonContent(button, ToolKey(initial), initial);
            foreach (var tool in variants) _toolAnchors.Add(tool, button);
            if (variants.Count == 1 && !includePrivacy)
            {
                _toolStrip.Children.Add(button);
                return;
            }

            var choices = new StackPanel { Spacing = 1 };
            foreach (var tool in variants) choices.Children.Add(_tools[tool]);
            if (includePrivacy) choices.Children.Add(_privacyDetect!);
            _toolGroupChoices.Add(choices);
            var flyout = new Flyout
            {
                OverlayDismissEventPassThrough = true,
                Content = new ThemeVariantScope { Child = choices },
            };
            _toolGroupFlyouts.Add(flyout);
            var drop = SmallButton(ToolKey(initial), () =>
            {
                var open = flyout.IsOpen;
                HideToolGroups();
                if (!open) flyout.ShowAt(button);
            });
            drop.Width = 20;
            drop.Padding = new(2, 0);
            drop.Content = DropDownChevron.Create(drop);
            _toolGroupDrops.Add((drop, variants.Contains(ScreenshotTool.Select)));
            _toolStrip.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Margin = new(0, 0, 2, 0), Children = { button, drop },
            });
        }

        private static void SetToolButtonContent(Button button, string key, ScreenshotTool? tool = null)
        {
            button.Height = AppTheme.CurrentAppearance.ToolbarIconSize switch
            {
                ToolbarIconSize.Small => 24, ToolbarIconSize.Large => 36, _ => 28,
            };
            var icon = ToolbarIcon.Create(button, tool is { } value
                ? ToolGeometry(value)
                : ToolbarIcon.TryGeometry(key) ?? ToolbarIcon.Geometry("AnimationEffects"));
            icon.Width = icon.Height = Math.Max(12, button.Height - 12);
            button.Content = icon;
            button.Width = button.Height;
            button.Padding = new(5);
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
        }

        private void RefreshToolGroups(ScreenshotTool tool)
        {
            if (_toolAnchors.TryGetValue(tool, out var current)) _groupTools[current] = tool;
            foreach (var (button, hasSelect) in _toolGroupDrops)
                button.IsEnabled = !_session.IsBusy && (_session.CanAnnotate || hasSelect);
            foreach (var (button, selected) in _groupTools)
                UpdateGroup(button, selected, _toolAnchors.GetValueOrDefault(tool) == button);
        }

        private void HideToolGroups()
        {
            foreach (var flyout in _toolGroupFlyouts) flyout.Hide();
        }
    }
}

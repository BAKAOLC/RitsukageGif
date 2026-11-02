using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotToolbar
    {
        private readonly Dictionary<ToolbarActionId, Button> _actionButtons = [];
        private readonly WrapPanel _functionStrip = new() { Orientation = Orientation.Horizontal };
        private readonly WrapPanel _moreActions = new() { Orientation = Orientation.Horizontal };

        private readonly ScrollViewer _moreScroll = new()
            { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        private readonly WrapPanel _toolStrip = new() { Orientation = Orientation.Horizontal };
        private object? _toolbarIdentity;
        private long _toolbarRevision = -1;

        private Button RegisterAction(ToolbarActionId id, Action action)
        {
            var definition = ToolbarActionCatalog.Get(id);
            var button = SmallButton(definition.NameKey, () =>
            {
                _moreCommands.Hide();
                action();
            }, ActionGeometry(definition));
            _actionButtons.Add(id, button);
            return button;
        }

        private static string ActionGeometry(ToolbarActionDefinition definition)
        {
            return ToolbarIcon.TryGeometry(definition.IconKey) ?? ToolbarIcon.Geometry("AnimationEffects");
        }

        private static Grid MenuContent(Button button, string key, string geometry, string? gesture)
        {
            var icon = ToolbarIcon.Create(button, geometry);
            var label = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                [!TextBlock.ForegroundProperty] = button[!TemplatedControl.ForegroundProperty],
            };
            label.Bind(TextBlock.TextProperty, LiveText.Resource(key));
            var shortcut = new TextBlock
            {
                Text = gesture is null ? null : KeyGestureDisplay.Format(gesture),
                Foreground = AppTheme.Ink,
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            };
            var content = new Grid { ColumnDefinitions = new("18,*,Auto"), ColumnSpacing = 8 };
            content.Children.Add(icon);
            Grid.SetColumn(label, 1);
            content.Children.Add(label);
            Grid.SetColumn(shortcut, 2);
            content.Children.Add(shortcut);
            return content;
        }

        private void InitializeCustomizationMenu()
        {
            var rows = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Stretch };
            rows.Children.Add(_moreActions);
            _moreScroll.Content = rows;
            var content = new StackPanel { Spacing = 2 };
            content.Children.Add(_moreScroll);
            content.Children.Add(new Border
                { Height = 1, Background = AppTheme.Outline, Margin = new(0, 4) });
            content.Children.Add(AdjustmentButton(false));
            _moreCommands.Content = new ThemeVariantScope
                { Child = content };
        }

        private Button AdjustmentButton(bool tools)
        {
            var button = MenuButton("ToolbarAdjust", () => _ = EditToolbarAsync(tools),
                ToolbarIcon.TryGeometry("GeneralHeading"));
            button.HorizontalAlignment = HorizontalAlignment.Right;
            button.Foreground = AppTheme.Muted;
            return button;
        }

        private void ShowMoreMenu()
        {
            var available = IsDocked ? double.PositiveInfinity : _monitor.Height / Scale;
            if (TopLevel.GetTopLevel(this) is Window owner)
            {
                if (!IsDocked) available = Math.Min(available, owner.Bounds.Height);
                if (owner.Screens.ScreenFromWindow(owner) is { } screen)
                    available = Math.Min(available, screen.WorkingArea.Height / owner.RenderScaling);
            }

            if (!double.IsFinite(available)) available = 720;
            _moreScroll.MaxHeight = Math.Max(48, Math.Min(420, available - 144));
            _moreCommands.ShowAt(_more);
        }

        private void RefreshToolbarPreferences()
        {
            var identity = _session.ToolbarPreferencesIdentity;
            if (_toolbarRevision == _session.ToolbarRevision && ReferenceEquals(identity, _toolbarIdentity)) return;
            _toolbarRevision = _session.ToolbarRevision;
            _toolbarIdentity = identity;
            var preferences = _session.ToolbarPreferences;
            RebuildToolColumns(preferences);
            _functionStrip.Children.Clear();
            _moreActions.Children.Clear();
            _moreActions.ItemWidth = _moreActions.ItemHeight = _more.Height + 4;
            foreach (var item in preferences.Actions)
            {
                if (!_actionButtons.TryGetValue(item.Id, out var button)) continue;
                var definition = ToolbarActionCatalog.Get(item.Id);
                button.IsVisible = true;
                switch (item.Visibility)
                {
                    case ToolbarButtonVisibility.Toolbar:
                        SetToolButtonContent(button, definition.IconKey);
                        _functionStrip.Children.Add(button);
                        break;
                    case ToolbarButtonVisibility.MoreMenu:
                        SetToolButtonContent(button, definition.IconKey);
                        button.Margin = new(2);
                        var command = ActionCommand(item.Id);
                        if (command is { } value) SetShortcutTip(button, value);
                        _moreActions.Children.Add(button);
                        break;
                    case ToolbarButtonVisibility.Hidden:
                        button.IsVisible = false;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(item.Visibility));
                }
            }

            _moreActions.Width = _moreActions.ItemWidth * Math.Min(5, _moreActions.Children.Count);
        }

        private static SelectionCommand? ActionCommand(ToolbarActionId id)
        {
            return ToolbarActionCatalog.Get(id).Command;
        }

        private async Task EditToolbarAsync(bool tools)
        {
            if (_disposed || TopLevel.GetTopLevel(this) is not Window owner) return;
            try
            {
                _moreCommands.Hide();
                HideToolGroups();
                using var scope = _dialogScopeProvider?.Invoke();
                Window editor = tools
                    ? new ToolbarToolsWindow(_session.ToolbarPreferences)
                    : new ToolbarActionsWindow(_session.ToolbarPreferences);
                if (await editor.ShowDialog<ToolbarPreferences?>(owner) is { } result && !_disposed)
                    _session.UpdateToolbarPreferences(result, tools);
            }
            catch (Exception error)
            {
                if (!_disposed) _session.SetExportState(false, error.Message);
            }
        }
    }
}

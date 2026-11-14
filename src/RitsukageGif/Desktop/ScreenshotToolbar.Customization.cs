using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotToolbar
    {
        private readonly Dictionary<ToolbarActionId, Button> _actionButtons = [];
        private readonly WrapPanel _functionStrip = new() { Orientation = Orientation.Horizontal };

        private readonly WrapPanel _moreActions = new()
            { Orientation = Orientation.Horizontal, ItemSpacing = 4, LineSpacing = 4 };

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
            if (definition.Configurable)
            {
                ToolTip.SetShowDelay(button, 1000);
                ToolTip.SetBetweenShowDelay(button, -1);
                ToolTip.SetShowOnDisabled(button, true);
            }

            _actionButtons.Add(id, button);
            return button;
        }

        private static IconDefinition ActionGeometry(ToolbarActionDefinition definition)
        {
            return ToolbarIcon.FindDefinition(definition.IconKey) ?? ToolbarIcon.GetDefinition("AnimationEffects");
        }

        private static void SetToolButtonContent(Button button, string key, bool inMoreMenu = false)
        {
            var iconSize = AppTheme.CurrentAppearance.ToolbarIconSize switch
            {
                ToolbarIconSize.Small => 12, ToolbarIconSize.Large => 24, _ => 16,
            };
            if (inMoreMenu) iconSize += 5;
            button.Height = iconSize + (inMoreMenu ? 10 : 12);
            var icon = ToolbarIcon.Create(button,
                ToolbarIcon.FindDefinition(key) ?? ToolbarIcon.GetDefinition("AnimationEffects"));
            icon.Width = icon.Height = iconSize;
            button.Content = icon;
            button.Width = button.Height;
            button.Padding = new(5);
            button.Margin = inMoreMenu ? new(0, 2) : new(0);
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            AnchoredSelectionTooltip.Configure(button, inMoreMenu);
        }

        private HoverPropertyPanel CreateMoreMenu()
        {
            return new(_more, CreateContent, _ => PrepareMoreMenu(), true);

            Control CreateContent()
            {
                var rows = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
                rows.Children.Add(_moreActions);
                _moreScroll.Content = rows;
                var content = new StackPanel { Spacing = 4 };
                content.Children.Add(_moreScroll);
                content.Children.Add(AdjustmentButton(false));
                return new ThemeVariantScope { Child = content };
            }
        }

        private Button AdjustmentButton(bool tools)
        {
            var button = MenuButton("ToolbarAdjust", () => _ = EditToolbarAsync(tools),
                ToolbarIcon.FindDefinition("GeneralHeading"));
            button.HorizontalAlignment = HorizontalAlignment.Right;
            button.Foreground = AppTheme.Muted;
            return button;
        }

        private void PrepareMoreMenu()
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
            _ = RefreshPrintCapabilitiesAsync();
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
            _moreActions.MaxWidth = (_more.Height + 3) * 5 + 4 * _moreActions.ItemSpacing;
            foreach (var item in preferences.Actions)
            {
                if (!ToolbarActionCatalog.Get(item.Id).Configurable) continue;
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
                        SetToolButtonContent(button, definition.IconKey, true);
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
                if (tools)
                {
                    var editor = new ToolbarToolsWindow(_session.ToolbarPreferences, result =>
                    {
                        if (!_disposed) _session.UpdateToolbarPreferences(result, true);
                    });
                    await editor.ShowDialog(owner);
                    return;
                }

                if (await new ToolbarActionsWindow(_session.ToolbarPreferences)
                        .ShowDialog<ToolbarPreferences?>(owner) is { } actions && !_disposed)
                    _session.UpdateToolbarPreferences(actions, false);
            }
            catch (Exception error)
            {
                if (!_disposed) _session.SetExportState(false, error.Message);
            }
        }
    }
}

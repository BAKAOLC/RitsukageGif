using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedGroupsManagerWindow : Window
    {
        private readonly Button _apply = new()
        {
            Name = "PinnedGroupApply", [!ContentProperty] = LiveText.Resource("PinnedGroupApply"),
        };

        private readonly Dictionary<string, TextBlock> _countLabels = new(StringComparer.Ordinal);

        private readonly TextBlock _defaultNotice = new()
        {
            [!TextBlock.TextProperty] = LiveText.Resource("PinnedGroupDefaultCannotRemove"),
            Foreground = AppTheme.Error, TextWrapping = TextWrapping.Wrap,
        };

        private readonly TextBlock _error = new()
        {
            Name = "PinnedGroupError", IsVisible = false, Foreground = AppTheme.Error, TextWrapping = TextWrapping.Wrap,
        };

        private readonly PinnedGroupEditorFields _fields = new(false);
        private readonly ListBox _groups = new() { Name = "PinnedGroupsList" };
        private readonly PinnedImageManager _manager;

        private readonly Button _remove = new()
        {
            Name = "PinnedGroupRemove", Foreground = AppTheme.Error,
            [!ContentProperty] = LiveText.Resource("PinnedGroupRemove"),
        };

        private string? _selectedGroup;

        internal PinnedGroupsManagerWindow(PinnedImageManager manager)
        {
            _manager = manager;
            InitializeGroupReordering();
            Width = 600;
            Height = 420;
            MinWidth = 520;
            MinHeight = 320;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Surface;
            this[!TitleProperty] = LiveText.Resource("PinnedGroupsManageTitle");
            var create = new Button
            {
                Name = "PinnedGroupCreate", [!ContentProperty] = LiveText.Resource("PinnedGroupCreateTitle"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            var left = new Grid { RowDefinitions = new("*,Auto"), RowSpacing = 8 };
            left.Children.Add(_groups);
            Grid.SetRow(create, 1);
            left.Children.Add(create);
            var clear = new Button
            {
                Name = "PinnedGroupClear", Foreground = AppTheme.Error,
                [!ContentProperty] = LiveText.Resource("PinnedGroupClear"),
            };
            var actions = new StackPanel
            {
                Spacing = 6, Children = { _remove, clear },
            };
            var right = new StackPanel
            {
                Spacing = 8,
                Children = { _fields, _error, _apply, _defaultNotice, actions },
            };
            var layout = new Grid
            {
                Margin = new(12), ColumnDefinitions = new("2*,3*"), ColumnSpacing = 12,
            };
            layout.Children.Add(left);
            Grid.SetColumn(right, 1);
            layout.Children.Add(right);
            Content = layout;
            _groups.SelectionChanged += (_, _) => LoadSelection();
            _fields.NameInput.TextChanged += (_, _) =>
            {
                _error.Text = "";
                _error.IsVisible = false;
                _apply.IsEnabled = _fields.GroupName.Length != 0;
            };
            _apply.Click += (_, _) => ApplyChanges();
            create.Click += async (_, _) =>
            {
                var name = await PinnedGroupEditorDialog.ShowAsync(_manager, this);
                if (name is not null) Reload(name);
            };
            _remove.Click += async (_, _) =>
            {
                if (_selectedGroup is { Length: > 0 } group)
                    await new PinnedGroupConfirmationDialog(_manager, group, true).ShowDialog<bool>(this);
            };
            clear.Click += async (_, _) =>
            {
                if (_selectedGroup is { } group)
                    await new PinnedGroupConfirmationDialog(_manager, group, false).ShowDialog<bool>(this);
            };
            _manager.GroupsChanged += OnGroupsChanged;
            _manager.Changed += OnManagerChanged;
            Closed += (_, _) =>
            {
                _manager.GroupsChanged -= OnGroupsChanged;
                _manager.Changed -= OnManagerChanged;
            };
            Reload(_manager.Preferences.ActiveGroup);
        }

        private void OnGroupsChanged()
        {
            Reload(_selectedGroup);
        }

        private void OnManagerChanged(object? sender, EventArgs args)
        {
            RefreshCounts();
        }

        private void RefreshCounts()
        {
            foreach (var (id, label) in _countLabels)
            {
                var counts = _manager.GetGroupPinCounts(id);
                label.Text = $"{counts.Visible}/{counts.Total}";
            }
        }

        private void Reload(string? selection)
        {
            CancelGroupDrag();
            _groupRows.Clear();
            _countLabels.Clear();
            _groups.Items.Clear();
            ListBoxItem? selected = null;
            foreach (var definition in _manager.GroupDefinitions)
            {
                var label = new TextBlock
                {
                    Text = definition.Name.Length == 0 ? UiText.Get("PinnedGroupDefault") : definition.Name,
                    TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                    FontWeight =
                        definition.Id == _manager.Preferences.ActiveGroup ? FontWeight.Bold : FontWeight.Normal,
                };
                var row = new Grid { ColumnDefinitions = new("20,*,Auto"), ColumnSpacing = 6 };
                row.Children.Add(PinnedGroupIcon.Create(this, definition,
                    definition.Id == _manager.Preferences.ActiveGroup));
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                var counts = _manager.GetGroupPinCounts(definition.Id);
                var count = new TextBlock
                {
                    Name = "PinnedGroupCount", Text = $"{counts.Visible}/{counts.Total}",
                    VerticalAlignment = VerticalAlignment.Center,
                };
                _countLabels.Add(definition.Id, count);
                Grid.SetColumn(count, 2);
                row.Children.Add(count);
                var item = new ListBoxItem
                {
                    Tag = definition.Id, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };
                item.Content = GroupDragRow(row, definition.Id, item);
                _groups.Items.Add(item);
                if (definition.Id == selection) selected = item;
            }

            _groups.SelectedItem = selected ?? (_groups.Items.Count == 0 ? null : _groups.Items[0]);
        }

        private void LoadSelection()
        {
            _selectedGroup = (string?)((ListBoxItem?)_groups.SelectedItem)?.Tag;
            if (_selectedGroup is not { } group) return;
            var definition = _manager.GroupDefinitions.First(value => value.Id == group);
            _fields.Load(definition.Name.Length == 0 ? UiText.Get("PinnedGroupDefault") : definition.Name,
                definition.Color);
            _remove.IsVisible = group.Length != 0;
            _defaultNotice.IsVisible = group.Length == 0;
            _error.Text = "";
            _error.IsVisible = false;
        }

        private void ApplyChanges()
        {
            if (_selectedGroup is not { } group) return;
            var name = _fields.GroupName;
            if (name.Length == 0)
            {
                _error.Text = UiText.Get("PinnedGroupNameEmpty");
                _error.IsVisible = true;
                return;
            }

            if (_manager.GroupDefinitions.Any(value => value.Name == name && value.Id != group))
            {
                _error.Text = UiText.Get("PinnedGroupNameExists");
                _error.IsVisible = true;
                return;
            }

            _manager.UpdateGroup(group, new() { Id = group, Name = name, Color = _fields.GroupColor });
            Reload(group);
        }
    }
}

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedGroupConfirmationDialog : Window
    {
        internal PinnedGroupConfirmationDialog(PinnedImageManager manager, string group, bool remove)
        {
            Width = 420;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Surface;
            this[!TitleProperty] = LiveText.Resource(remove ? "PinnedGroupRemoveConfirm" : "PinnedGroupClearConfirm");
            var selected = manager.GroupDefinitions.First(value => value.Id == group);
            var displayName = selected.Name.Length == 0 ? UiText.Get("PinnedGroupDefault") : selected.Name;
            var warning = new TextBlock
            {
                [!TextBlock.TextProperty] = LiveText.Resource("PinnedGroupRemovePinsWarning"),
                Foreground = AppTheme.Error,
                TextWrapping = TextWrapping.Wrap,
            };
            var panel = new StackPanel
            {
                Margin = new(12), Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = UiText.Format(remove ? "PinnedGroupRemoveQuestion" : "PinnedGroupClearQuestion",
                            displayName),
                        TextWrapping = TextWrapping.Wrap,
                    },
                    warning,
                },
            };
            var move = new CheckBox { [!ContentProperty] = LiveText.Resource("PinnedGroupMovePins") };
            var destinations = new ComboBox { Name = "PinnedGroupMoveDestination", IsEnabled = false };
            if (remove)
            {
                foreach (var definition in manager.GroupDefinitions.Where(value => value.Id != group))
                    destinations.Items.Add(new ComboBoxItem
                    {
                        Content = definition.Name.Length == 0 ? UiText.Get("PinnedGroupDefault") : definition.Name,
                        Tag = definition.Id,
                    });
                destinations.SelectedIndex = 0;
                move.IsCheckedChanged += (_, _) =>
                {
                    destinations.IsEnabled = move.IsChecked == true;
                    warning.IsVisible = move.IsChecked != true;
                };
                panel.Children.Add(move);
                panel.Children.Add(destinations);
            }

            var confirm = new Button
            {
                Name = "PinnedGroupConfirm", Foreground = AppTheme.Error,
                Content = UiText.Format(remove ? "PinnedGroupRemoveNamed" : "PinnedGroupClearNamed", displayName),
            };
            var cancel = new Button { [!ContentProperty] = LiveText.Resource("EditorCancel"), IsCancel = true };
            confirm.Click += (_, _) =>
            {
                if (remove)
                    manager.RemoveGroup(group,
                        move.IsChecked == true ? ((ComboBoxItem?)destinations.SelectedItem)?.Tag as string : null);
                else
                    manager.ClearGroup(group);
                Close(true);
            };
            cancel.Click += (_, _) => Close(false);
            panel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 6, Children = { confirm, cancel },
            });
            Content = panel;
        }
    }
}

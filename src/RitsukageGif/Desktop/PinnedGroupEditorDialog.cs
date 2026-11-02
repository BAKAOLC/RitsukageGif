using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedGroupEditorDialog : Window
    {
        private string? _createdGroup;

        private PinnedGroupEditorDialog(PinnedImageManager manager)
        {
            Width = 360;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Surface;
            this[!TitleProperty] = LiveText.Resource("PinnedGroupCreateTitle");
            var fields = new PinnedGroupEditorFields(true);
            var switchToNew = new CheckBox
            {
                Name = "PinnedGroupSwitchNew", [!ContentProperty] = LiveText.Resource("PinnedGroupSwitchNew"),
            };
            var error = new TextBlock
            {
                Name = "PinnedGroupError", IsVisible = false, Foreground = AppTheme.Error,
                TextWrapping = TextWrapping.Wrap,
            };
            var create = new Button
            {
                Name = "PinnedGroupCreate", [!ContentProperty] = LiveText.Resource("PinnedGroupCreate"),
                IsDefault = true, IsEnabled = false,
            };
            create.Classes.Add("primary");
            var cancel = new Button { [!ContentProperty] = LiveText.Resource("EditorCancel"), IsCancel = true };
            fields.NameInput.TextChanged += (_, _) =>
            {
                error.Text = "";
                error.IsVisible = false;
                create.IsEnabled = fields.GroupName.Length != 0;
            };
            create.Click += (_, _) =>
            {
                var name = fields.GroupName;
                if (name.Length == 0)
                {
                    error.Text = UiText.Get("PinnedGroupNameEmpty");
                    error.IsVisible = true;
                    return;
                }

                if (manager.GroupDefinitions.Any(value => value.Name == name))
                {
                    error.Text = UiText.Get("PinnedGroupNameExists");
                    error.IsVisible = true;
                    return;
                }

                var definition = new PinnedGroupDefinition { Name = name, Color = fields.GroupColor };
                manager.CreateGroup(definition, switchToNew.IsChecked == true);
                _createdGroup = definition.Id;
                Close(definition.Id);
            };
            cancel.Click += (_, _) => Close();
            Content = new StackPanel
            {
                Margin = new(12), Spacing = 8,
                Children =
                {
                    fields, switchToNew, error,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 6, Children = { create, cancel },
                    },
                },
            };
            Opened += (_, _) => fields.NameInput.Focus();
        }

        internal static Task<string?> ShowAsync(PinnedImageManager manager, Window? owner = null,
            Action<Window>? onCreated = null)
        {
            var dialog = new PinnedGroupEditorDialog(manager);
            onCreated?.Invoke(dialog);
            if (owner is not null) return dialog.ShowDialog<string?>(owner);
            var completion = new TaskCompletionSource<string?>();
            dialog.Closed += (_, _) => completion.SetResult(dialog._createdGroup);
            dialog.Show();
            return completion.Task;
        }
    }
}

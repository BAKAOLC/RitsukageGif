using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private void AddHotkeyPolicyControls(AppSettings draft, StackPanel footer, Button ignored)
        {
            var fullscreen = new CheckBox
            {
                [!ContentProperty] = LiveText.Resource("HotkeyDisableInFullscreen"),
                IsChecked = draft.HotkeyPolicy.DisableInFullscreen,
                IsEnabled = OperatingSystem.IsWindows(),
            };
            ignored.IsEnabled = OperatingSystem.IsWindows();
            if (!OperatingSystem.IsWindows())
            {
                fullscreen.Bind(ToolTip.TipProperty, LiveText.Resource("HotkeyPolicyUnavailable"));
                ignored.Bind(ToolTip.TipProperty, LiveText.Resource("HotkeyPolicyUnavailable"));
            }

            fullscreen.IsCheckedChanged += (_, _) => draft.HotkeyPolicy = draft.HotkeyPolicy with
            {
                DisableInFullscreen = fullscreen.IsChecked == true,
            };
            footer.Children.Add(fullscreen);
            ignored.Click += async (_, _) =>
            {
                var programs = await EditIgnoredProgramsAsync(draft.HotkeyPolicy.IgnoredPrograms);
                if (programs is null) return;
                draft.HotkeyPolicy = draft.HotkeyPolicy with { IgnoredPrograms = programs };
                Refresh();
                RequestLiveApply();
            };
            SubscribeContentLanguage(Refresh);
            Refresh();
            return;

            void Refresh()
            {
                ignored.Content = string.Format(UiText.Get("HotkeyIgnoredPrograms"),
                    draft.HotkeyPolicy.IgnoredPrograms.Count);
            }
        }

        private async Task<List<string>?> EditIgnoredProgramsAsync(IReadOnlyList<string> programs)
        {
            var pending = programs.ToList();
            var dialog = new Window
            {
                Name = "IgnoredProgramsEditor", Title = UiText.Get("HotkeyIgnoredProgramsTitle"),
                Width = 600, Height = 440, MinWidth = 460, MinHeight = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = AppTheme.Surface, Foreground = AppTheme.Ink,
            };
            dialog.Classes.Add("settings-window");
            var input = new TextBox
            {
                Name = "IgnoredProgramInput", Watermark = UiText.Get("HotkeyIgnoredProgramsHint"),
            };
            var add = ActionPageButton("HotkeyIgnoredProgramAdd");
            var browse = ActionPageButton("Browse");
            var inputRow = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8 };
            inputRow.Children.Add(input);
            Grid.SetColumn(browse, 1);
            inputRow.Children.Add(browse);
            Grid.SetColumn(add, 2);
            inputRow.Children.Add(add);
            var rows = new StackPanel { Name = "IgnoredProgramRows", Spacing = 4 };
            var error = new TextBlock { Foreground = AppTheme.Error, TextWrapping = TextWrapping.Wrap };
            var save = ActionPageButton("GlobalActionApply");
            var cancel = ActionPageButton("Cancel");
            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                Children = { cancel, save },
            };
            var layout = new Grid { RowDefinitions = new("Auto,Auto,*,Auto"), RowSpacing = 12, Margin = new(16) };
            layout.Children.Add(inputRow);
            Grid.SetRow(error, 1);
            layout.Children.Add(error);
            var scroll = new ScrollViewer
            {
                Content = rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            Grid.SetRow(scroll, 2);
            layout.Children.Add(scroll);
            Grid.SetRow(footer, 3);
            layout.Children.Add(footer);
            dialog.Content = layout;
            add.Click += (_, _) => AddProgram(input.Text ?? "");
            browse.Click += async (_, _) =>
            {
                var files = await dialog.StorageProvider.OpenFilePickerAsync(new()
                {
                    Title = UiText.Get("HotkeyIgnoredProgramsTitle"), AllowMultiple = true,
                    FileTypeFilter = [new(UiText.Get("HotkeyIgnoredProgramsTitle")) { Patterns = ["*.exe"] }],
                });
                foreach (var file in files)
                    if (file.TryGetLocalPath() is { } path)
                        AddProgram(path);
            };
            save.Click += (_, _) => dialog.Close(pending.ToList());
            cancel.Click += (_, _) => dialog.Close();
            Refresh();
            return await dialog.ShowDialog<List<string>?>(this);

            void AddProgram(string value)
            {
                if (!HotkeyPolicyPreferences.TryNormalizeProgram(value, out var normalized))
                {
                    error.Text = UiText.Get("HotkeyIgnoredProgramInvalid");
                    return;
                }

                if (!pending.Contains(normalized, StringComparer.OrdinalIgnoreCase)) pending.Add(normalized);
                input.Text = "";
                error.Text = "";
                Refresh();
            }

            void Refresh()
            {
                rows.Children.Clear();
                foreach (var program in pending.ToArray())
                {
                    var row = new Grid { ColumnDefinitions = new("*,28"), ColumnSpacing = 8, MinHeight = 36 };
                    row.Children.Add(new TextBlock
                    {
                        Text = program, TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                    ToolTip.SetTip(row, program);
                    var remove = BindingButton("SelectionShortcutRemove", "M6,6 L18,18 M18,6 L6,18");
                    remove.Foreground = AppTheme.Error;
                    Grid.SetColumn(remove, 1);
                    row.Children.Add(remove);
                    rows.Children.Add(ActionRowBorder(row));
                    remove.Click += (_, _) =>
                    {
                        pending.Remove(program);
                        Refresh();
                    };
                }
            }
        }
    }
}

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private static StackPanel CleanupPage(AppSettings draft, out Action apply)
        {
            var page = Page();
            var enabled = new CheckBox
            {
                Name = "CleanupEnabled", Content = UiText.Get("CleanupEnabled"), IsChecked = draft.CleanupEnabled
            };
            page.Children.Add(enabled);
            var rules = new StackPanel { Spacing = 8, IsEnabled = enabled.IsChecked == true };
            enabled.IsCheckedChanged += (_, _) => rules.IsEnabled = enabled.IsChecked == true;
            page.Children.Add(rules);

            Func<int?> Rule(string key, int? value, int suggested)
            {
                var checkbox = new CheckBox
                {
                    Name = key + "Enabled", Content = UiText.Get(key), IsChecked = value.HasValue,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var number = new CompactNumberInput
                {
                    Name = key, Minimum = 1, Maximum = int.MaxValue, Value = value ?? suggested,
                    Width = 100, Height = 28, IsEnabled = checkbox.IsChecked == true,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
                };
                checkbox.IsCheckedChanged += (_, _) => number.IsEnabled = checkbox.IsChecked == true;
                var row = new Grid { ColumnDefinitions = new("*,Auto") };
                row.Children.Add(checkbox);
                Grid.SetColumn(number, 1);
                row.Children.Add(number);
                rules.Children.Add(row);
                return () => checkbox.IsChecked == true ? (int)(number.Value ?? suggested) : null;
            }

            var age = Rule("CleanupMaxAgeDays", draft.CleanupMaxAgeDays, 30);
            var files = Rule("CleanupMaxFiles", draft.CleanupMaxFiles, 100);
            var size = Rule("CleanupMaxSizeMiB", draft.CleanupMaxSizeMiB, 1024);
            page.Children.Add(new TextBlock
            {
                Text = UiText.Get("CleanupRulesHint"), FontSize = 12, Foreground = Brushes.SlateGray,
                TextWrapping = TextWrapping.Wrap
            });
            page.Children.Add(new TextBlock
            {
                Text = UiText.Get("CleanupScopeHint"), FontSize = 12, Foreground = Brushes.SlateGray,
                TextWrapping = TextWrapping.Wrap
            });
            apply = () =>
            {
                draft.CleanupEnabled = enabled.IsChecked == true;
                draft.CleanupMaxAgeDays = age();
                draft.CleanupMaxFiles = files();
                draft.CleanupMaxSizeMiB = size();
            };
            return page;
        }
    }
}

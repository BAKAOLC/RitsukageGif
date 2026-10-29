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
                Name = "CleanupEnabled", [!ContentProperty] = LiveText.Resource("CleanupEnabled"),
                IsChecked = draft.CleanupEnabled,
            };
            page.Children.Add(enabled);
            var rules = new StackPanel { Spacing = 8, IsEnabled = enabled.IsChecked == true };
            enabled.IsCheckedChanged += (_, _) => rules.IsEnabled = enabled.IsChecked == true;
            page.Children.Add(rules);

            var age = Rule("CleanupMaxAgeDays", draft.CleanupMaxAgeDays, 30);
            var files = Rule("CleanupMaxFiles", draft.CleanupMaxFiles, 100);
            var size = Rule("CleanupMaxSizeMiB", draft.CleanupMaxSizeMiB, 1024);
            page.Children.Add(new TextBlock
            {
                [!TextBlock.TextProperty] = LiveText.Resource("CleanupRulesHint"), FontSize = 12,
                Foreground = AppTheme.Muted,
                TextWrapping = TextWrapping.Wrap,
            });
            page.Children.Add(new TextBlock
            {
                [!TextBlock.TextProperty] = LiveText.Resource("CleanupScopeHint"), FontSize = 12,
                Foreground = AppTheme.Muted,
                TextWrapping = TextWrapping.Wrap,
            });
            apply = () =>
            {
                draft.CleanupEnabled = enabled.IsChecked == true;
                draft.CleanupMaxAgeDays = age();
                draft.CleanupMaxFiles = files();
                draft.CleanupMaxSizeMiB = size();
            };
            return page;

            Func<int?> Rule(string key, int? value, int suggested)
            {
                var checkbox = new CheckBox
                {
                    Name = key + "Enabled",
                    Content = new TextBlock
                        { [!TextBlock.TextProperty] = LiveText.Resource(key), TextWrapping = TextWrapping.Wrap },
                    IsChecked = value.HasValue, Margin = new(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var number = new CompactNumberInput
                {
                    Name = key, Minimum = 1, Maximum = int.MaxValue, Value = value ?? suggested,
                    Width = 100, Height = 28, IsEnabled = checkbox.IsChecked == true,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                };
                checkbox.IsCheckedChanged += (_, _) => number.IsEnabled = checkbox.IsChecked == true;
                var row = new Grid { ColumnDefinitions = new("*,Auto") };
                row.Children.Add(checkbox);
                Grid.SetColumn(number, 1);
                row.Children.Add(number);
                rules.Children.Add(row);
                return () => checkbox.IsChecked == true ? (int)ReadSettingsNumber(number) : null;
            }
        }
    }
}

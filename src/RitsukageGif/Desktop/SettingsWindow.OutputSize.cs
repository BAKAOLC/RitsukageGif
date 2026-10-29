using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private WrapPanel OutputSizeEditor(ScreenshotOutputSize value, out Func<ScreenshotOutputSize> read)
        {
            value = value.Normalize();
            var mode = new ComboBox { SelectedIndex = (int)value.Mode, MinWidth = 118 };
            var percent = new NumericUpDown
                { Minimum = 0.01m, Maximum = 10000, Value = (decimal)value.Percent, Width = 100 };
            var width = new NumericUpDown
                { Minimum = 1, Maximum = 128 * 1024 * 1024, Value = value.Width, Width = 110 };
            var height = new NumericUpDown
                { Minimum = 1, Maximum = 128 * 1024 * 1024, Value = value.Height, Width = 110 };
            var percentage = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            percentage.Children.Add(percent);
            percentage.Children.Add(new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center });
            var dimensions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            dimensions.Children.Add(width);
            dimensions.Children.Add(new TextBlock { Text = "×", VerticalAlignment = VerticalAlignment.Center });
            dimensions.Children.Add(height);
            dimensions.Children.Add(new TextBlock { Text = "px", VerticalAlignment = VerticalAlignment.Center });
            var row = new WrapPanel { Orientation = Orientation.Horizontal };
            mode.Margin = new(0, 0, 8, 0);
            row.Children.Add(mode);
            row.Children.Add(percentage);
            row.Children.Add(dimensions);
            mode.SelectionChanged += (_, _) => UpdateVisibility();
            SubscribeContentLanguage(RefreshLanguage);
            RefreshLanguage();
            UpdateVisibility();
            read = () => new()
            {
                Mode = (ScreenshotOutputSizeMode)Math.Max(0, mode.SelectedIndex),
                Percent = mode.SelectedIndex == (int)ScreenshotOutputSizeMode.Percent
                    ? (double)ReadSettingsNumber(percent)
                    : (double)(percent.Value ?? (decimal)value.Percent),
                Width = dimensions.IsVisible ? (int)ReadSettingsNumber(width) : (int)(width.Value ?? value.Width),
                Height = dimensions.IsVisible ? (int)ReadSettingsNumber(height) : (int)(height.Value ?? value.Height),
            };
            return row;

            void UpdateVisibility()
            {
                percentage.IsVisible = mode.SelectedIndex == (int)ScreenshotOutputSizeMode.Percent;
                dimensions.IsVisible =
                    mode.SelectedIndex is (int)ScreenshotOutputSizeMode.Fit or (int)ScreenshotOutputSizeMode.Exact;
            }

            void RefreshLanguage()
            {
                var selected = mode.SelectedIndex;
                mode.ItemsSource = new[]
                {
                    UiText.Get("ScreenshotSizeOriginal"), UiText.Get("ScreenshotSizePercent"),
                    UiText.Get("ScreenshotSizeFit"), UiText.Get("ScreenshotSizeExact"),
                };
                mode.SelectedIndex = selected;
            }
        }
    }
}

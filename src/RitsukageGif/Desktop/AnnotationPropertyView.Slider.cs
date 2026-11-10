using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        private Grid PropertySlider(string key, double minimum, double maximum, Func<double> read,
            Func<double, Task> update, string suffix = "")
        {
            var row = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 6 };
            row.Children.Add(new TextBlock { Text = UiText.Get(key), VerticalAlignment = VerticalAlignment.Center });
            var slider = new Slider { Minimum = minimum, Maximum = maximum, SmallChange = 1, Width = 120 };
            Grid.SetColumn(slider, 1);
            row.Children.Add(slider);
            var value = new TextBlock { MinWidth = 32, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(value, 2);
            row.Children.Add(value);
            slider.ValueChanged += async (_, _) =>
            {
                if (!_disposed && !_refreshing) await update(Math.Round(slider.Value));
            };
            _refresh.Add(() =>
            {
                slider.Value = read();
                value.Text = Math.Round(slider.Value).ToString(CultureInfo.CurrentCulture) + suffix;
            });
            return row;
        }
    }
}

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private static Grid PolygonCornerEditor(int value, out Func<int> read)
        {
            var slider = new Slider
            {
                Name = "CapturePolygonCornerRadius", Minimum = 0, Maximum = 10, TickFrequency = 1,
                IsSnapToTickEnabled = true, Value = value, MinWidth = 120,
            };
            var number = new TextBlock
            {
                Text = value.ToString(CultureInfo.CurrentCulture), VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var panel = new Grid { ColumnDefinitions = new("*,24"), ColumnSpacing = 8 };
            panel.Children.Add(slider);
            Grid.SetColumn(number, 1);
            panel.Children.Add(number);
            slider.ValueChanged += (_, _) => number.Text = ((int)Math.Round(slider.Value))
                .ToString(CultureInfo.CurrentCulture);
            read = () => (int)Math.Round(slider.Value);
            return panel;
        }
    }
}

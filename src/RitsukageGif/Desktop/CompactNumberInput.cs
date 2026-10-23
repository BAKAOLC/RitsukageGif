using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    // Compact spinner keeps the original WPF footprint without relying on a theme's
    // minimum touch-target sizes or horizontal increment buttons.
    internal sealed class CompactNumberInput : UserControl
    {
        private readonly TextBox _text = new()
        {
            FontSize = 12, Padding = new Thickness(1, 0), MinHeight = 0,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.White, BorderThickness = new Thickness(0)
        };

        private int _last = 1;
        public int Minimum { get; init; } = 1;
        public int Maximum { get; init; } = 100;

        public CompactNumberInput()
        {
            Width = 50;
            Height = 22;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14") };
            grid.Children.Add(_text);
            var buttons = new Grid { RowDefinitions = new RowDefinitions("*,*") };
            Grid.SetColumn(buttons, 1);
            for (var i = 0; i < 2; i++)
            {
                var increment = i == 0 ? 1 : -1;
                var button = new Button
                {
                    Content = i == 0 ? "▴" : "▾", FontSize = 9, Padding = new Thickness(0),
                    MinWidth = 0, MinHeight = 0, CornerRadius = new CornerRadius(0),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                button.Click += (_, _) => Value = Value + increment;
                Grid.SetRow(button, i);
                buttons.Children.Add(button);
            }

            grid.Children.Add(buttons);
            Content = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = grid };
            _text.LostFocus += (_, _) => _ = Value;
        }

        public decimal? Value
        {
            get
            {
                if (int.TryParse(_text.Text, out var value)) _last = Math.Clamp(value, Minimum, Maximum);
                _text.Text = _last.ToString();
                return _last;
            }
            set
            {
                _last = Math.Clamp((int)(value ?? Minimum), Minimum, Maximum);
                _text.Text = _last.ToString();
            }
        }
    }
}

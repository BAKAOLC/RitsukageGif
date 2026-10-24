using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
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
            Padding = new Thickness(1, 0), MinHeight = 0, Height = double.NaN,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0)
        };

        private int _last = 1;
        public int Minimum { get; init; } = 1;
        public int Maximum { get; init; } = 100;

        static CompactNumberInput()
        {
            BackgroundProperty.OverrideDefaultValue<CompactNumberInput>(Brushes.White);
            BorderBrushProperty.OverrideDefaultValue<CompactNumberInput>(new SolidColorBrush(Color.Parse("#CBD5E1")));
            BorderThicknessProperty.OverrideDefaultValue<CompactNumberInput>(new Thickness(1));
            CornerRadiusProperty.OverrideDefaultValue<CompactNumberInput>(new CornerRadius(4));
        }

        public CompactNumberInput()
        {
            FontSize = 12;
            _text[!FontSizeProperty] = this[!FontSizeProperty];
            Width = 50;
            Height = 22;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,18") };
            grid.Children.Add(_text);
            var buttons = new Grid { RowDefinitions = new RowDefinitions("*,*") };
            Grid.SetColumn(buttons, 1);
            for (var i = 0; i < 2; i++)
            {
                var increment = i == 0 ? 1 : -1;
                var button = new Button
                {
                    Padding = new Thickness(0),
                    MinWidth = 0, MinHeight = 0, Height = double.NaN, CornerRadius = new CornerRadius(0),
                    BorderThickness = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Template = new FuncControlTemplate<Button>((owner, _) => new ContentPresenter
                    {
                        [!ContentPresenter.ContentProperty] = owner[!ContentProperty],
                        [!ContentPresenter.BackgroundProperty] = owner[!BackgroundProperty],
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center
                    })
                };
                button.Classes.Add("number-stepper");
                button.Content = new Avalonia.Controls.Shapes.Path
                {
                    Data = Geometry.Parse(i == 0 ? "M0,4 L4,0 8,4" : "M0,0 L4,4 8,0"),
                    Width = 8, Height = 4, StrokeThickness = 1.5,
                    [!Avalonia.Controls.Shapes.Shape.StrokeProperty] = button[!ForegroundProperty]
                };
                button.Bind(Avalonia.Automation.AutomationProperties.NameProperty,
                    LiveText.Resource(i == 0 ? "NumericIncrease" : "NumericDecrease"));
                button.Click += (_, _) => Value = Value + increment;
                Grid.SetRow(button, i);
                buttons.Children.Add(button);
            }

            grid.Children.Add(buttons);
            Content = new Border
            {
                [!Border.BorderBrushProperty] = this[!BorderBrushProperty],
                [!Border.BorderThicknessProperty] = this[!BorderThicknessProperty],
                [!Border.BackgroundProperty] = this[!BackgroundProperty],
                [!Border.CornerRadiusProperty] = this[!CornerRadiusProperty],
                ClipToBounds = true, Child = grid
            };
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

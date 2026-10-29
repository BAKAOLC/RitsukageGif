using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace RitsukageGif.Desktop
{
    // Compact spinner keeps the original WPF footprint without relying on a theme's
    // minimum touch-target sizes or horizontal increment buttons.
    internal sealed class CompactNumberInput : UserControl
    {
        private readonly TextBox _text = new()
        {
            Padding = new(1, 0), MinHeight = 0, Height = double.NaN,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent, BorderThickness = new(0), CornerRadius = new(0),
        };

        private decimal _last = 1;
        private bool _updatingText;

        static CompactNumberInput()
        {
            BackgroundProperty.OverrideDefaultValue<CompactNumberInput>(Brushes.White);
            BorderBrushProperty.OverrideDefaultValue<CompactNumberInput>(new SolidColorBrush(Color.Parse("#CBD5E1")));
            BorderThicknessProperty.OverrideDefaultValue<CompactNumberInput>(new(1));
            CornerRadiusProperty.OverrideDefaultValue<CompactNumberInput>(new(4));
        }

        public CompactNumberInput()
        {
            Classes.Add("compact-number-input");
            FontSize = 12;
            _text[!FontSizeProperty] = this[!FontSizeProperty];
            _text[!ForegroundProperty] = this[!ForegroundProperty];
            Width = 50;
            Height = 22;
            var grid = new Grid { ColumnDefinitions = new("*,18") };
            grid.Children.Add(_text);
            var buttons = new Grid { RowDefinitions = new("*,*") };
            Grid.SetColumn(buttons, 1);
            for (var i = 0; i < 2; i++)
            {
                var increment = i == 0 ? 1 : -1;
                var button = new Button
                {
                    Background = Brushes.Transparent,
                    [!ForegroundProperty] = this[!ForegroundProperty],
                    Padding = new(0),
                    MinWidth = 0, MinHeight = 0, Height = double.NaN, CornerRadius = new(0),
                    BorderThickness = new(0),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Template = new FuncControlTemplate<Button>((owner, _) => new ContentPresenter
                    {
                        [!ContentPresenter.ContentProperty] = owner[!ContentProperty],
                        [!ContentPresenter.BackgroundProperty] = owner[!BackgroundProperty],
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center,
                    }),
                };
                button.Classes.Add("number-stepper");
                button.Content = new Path
                {
                    Data = Geometry.Parse(i == 0 ? "M0,4 L4,0 8,4" : "M0,0 L4,4 8,0"),
                    Width = 8, Height = 4, StrokeThickness = 1.5,
                    [!Shape.StrokeProperty] = button[!ForegroundProperty],
                };
                button.Bind(AutomationProperties.NameProperty,
                    LiveText.Resource(i == 0 ? "NumericIncrease" : "NumericDecrease"));
                button.Click += (_, _) => Step(increment);
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
                ClipToBounds = true, Child = grid,
            };
            _text.TextChanged += (_, _) =>
            {
                if (_updatingText || !TryRead(out var value) || value < Minimum || value > Maximum) return;
                SetValue(value, false);
            };
            _text.LostFocus += (_, _) => SetValue(Value ?? Minimum, true);
        }

        public decimal Minimum { get; init; } = 1;
        public decimal Maximum { get; init; } = 100;

        public int DecimalPlaces
        {
            get;
            init => field = value is >= 0 and <= 28 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }

        public decimal Increment
        {
            get;
            init => field = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        } = 1;

        public decimal? Value
        {
            get
            {
                if (TryRead(out var value)) SetValue(value, false);
                else WriteText();
                return _last;
            }
            set => SetValue(value ?? Minimum, true);
        }

        internal bool TryGetInputValue(out decimal value)
        {
            if (!TryRead(out value) || value < Minimum || value > Maximum) return false;
            value = DecimalPlaces == 0
                ? decimal.Truncate(value)
                : decimal.Round(value, DecimalPlaces, MidpointRounding.AwayFromZero);
            return true;
        }

        public event EventHandler? ValueChanged;

        private bool TryRead(out decimal value)
        {
            if (DecimalPlaces == 0)
            {
                var valid = decimal.TryParse(_text.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value);
                return valid;
            }

            return decimal.TryParse(_text.Text,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite |
                NumberStyles.AllowTrailingWhite, CultureInfo.CurrentCulture, out value);
        }

        private void SetValue(decimal value, bool write)
        {
            value = DecimalPlaces == 0
                ? decimal.Truncate(value)
                : decimal.Round(value, DecimalPlaces, MidpointRounding.AwayFromZero);
            var next = Math.Clamp(value, Minimum, Maximum);
            var changed = next != _last;
            _last = next;
            if (write) WriteText();
            if (changed) ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        private void WriteText()
        {
            _updatingText = true;
            try
            {
                _text.Text = _last.ToString(DecimalPlaces == 0 ? "0" : "0." + new string('#', DecimalPlaces),
                    CultureInfo.CurrentCulture);
            }
            finally
            {
                _updatingText = false;
            }
        }

        private void Step(int direction)
        {
            decimal next;
            try
            {
                next = (Value ?? Minimum) + direction * Increment;
            }
            catch (OverflowException)
            {
                next = direction > 0 ? Maximum : Minimum;
            }

            Value = next;
        }
    }
}

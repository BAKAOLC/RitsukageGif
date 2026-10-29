using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionFrameControls
    {
        private readonly RadioButton _border = new() { MinHeight = 26, VerticalAlignment = VerticalAlignment.Center };
        private readonly AppearanceColorPicker _borderColor = ColorPicker();
        private readonly Border _borderSwatch = Swatch();
        private readonly Slider _radius = Slider(0, 60);
        private readonly NumericUpDown _radiusNumber = Number(0, 60);
        private readonly CheckBox _remember = new() { MinHeight = 26, VerticalAlignment = VerticalAlignment.Center };
        private readonly RadioButton _shadow = new() { MinHeight = 26, VerticalAlignment = VerticalAlignment.Center };
        private readonly AppearanceColorPicker _shadowColor = ColorPicker();
        private readonly Border _shadowSwatch = Swatch();
        private readonly Slider _strength = Slider(1, 50);
        private readonly NumericUpDown _strengthNumber = Number(1, 50);

        private StackPanel CreateRoundPanel()
        {
            _radius.ValueChanged += (_, args) =>
            {
                if (!_updating) _coordinator.SetFrameRadius(args.NewValue);
            };
            _radiusNumber.ValueChanged += (_, args) =>
            {
                if (!_updating && args.NewValue is { } value) _coordinator.SetFrameRadius((double)value);
            };
            return new()
            {
                Spacing = 6,
                Children = { Label("SelectionFrameCornerRadius"), ValueRow(_radius, _radiusNumber) },
            };
        }

        private StackPanel CreateDecorationPanel()
        {
            _shadowColor.DialogScopeProvider = _coordinator.BeginSelectionDialog;
            _borderColor.DialogScopeProvider = _coordinator.BeginSelectionDialog;
            _shadowColor.Content = _shadowSwatch;
            _borderColor.Content = _borderSwatch;
            _shadow.Bind(ContentControl.ContentProperty, LiveText.Resource("SelectionFrameShadow"));
            _border.Bind(ContentControl.ContentProperty, LiveText.Resource("SelectionFrameBorder"));
            _remember.Bind(ContentControl.ContentProperty, LiveText.Resource("SelectionFrameRemember"));
            _shadow.Click += (_, _) =>
            {
                if (!_updating) _coordinator.SetFrameDecorationKind(CaptureDecorationKind.Shadow);
            };
            _border.Click += (_, _) =>
            {
                if (!_updating) _coordinator.SetFrameDecorationKind(CaptureDecorationKind.Border);
            };
            _remember.Click += (_, _) =>
            {
                if (!_updating) _coordinator.KeepDecoration = _remember.IsChecked == true;
            };
            _strength.ValueChanged += (_, args) =>
            {
                if (!_updating) _coordinator.SetFrameDecorationStrength((int)Math.Round(args.NewValue));
            };
            _strengthNumber.ValueChanged += (_, args) =>
            {
                if (!_updating && args.NewValue is { } value)
                    _coordinator.SetFrameDecorationStrength((int)value);
            };
            _shadowColor.ColorChanged += (_, args) =>
            {
                if (!_updating)
                    _coordinator.SetFrameDecorationColor(CaptureDecorationKind.Shadow, args.NewColor.ToUInt32());
            };
            _borderColor.ColorChanged += (_, args) =>
            {
                if (!_updating)
                    _coordinator.SetFrameDecorationColor(CaptureDecorationKind.Border, args.NewColor.ToUInt32());
            };
            return new()
            {
                Spacing = 6,
                Children =
                {
                    ModeRow(_shadow, _shadowColor), ModeRow(_border, _borderColor),
                    Label("SelectionFrameStrength"), ValueRow(_strength, _strengthNumber), _remember,
                },
            };
        }

        private void UpdateProperties()
        {
            _radius.Value = _coordinator.FrameCornerRadius;
            _radiusNumber.Value = (decimal)_coordinator.FrameCornerRadius;
            _strength.Value = _coordinator.FrameDecorationStrength;
            _strengthNumber.Value = _coordinator.FrameDecorationStrength;
            _shadow.IsChecked = _coordinator.FrameDecorationKind == CaptureDecorationKind.Shadow;
            _border.IsChecked = _coordinator.FrameDecorationKind == CaptureDecorationKind.Border;
            _shadowColor.Color = Color.FromUInt32(_coordinator.GetFrameDecorationColor(CaptureDecorationKind.Shadow));
            _borderColor.Color = Color.FromUInt32(_coordinator.GetFrameDecorationColor(CaptureDecorationKind.Border));
            _remember.IsChecked = _coordinator.KeepDecoration;
            if (_shadowSwatch.Background is not SolidColorBrush shadow || shadow.Color != _shadowColor.Color)
                _shadowSwatch.Background = new SolidColorBrush(_shadowColor.Color);
            if (_borderSwatch.Background is not SolidColorBrush border || border.Color != _borderColor.Color)
                _borderSwatch.Background = new SolidColorBrush(_borderColor.Color);
        }

        private static Border Swatch()
        {
            return new()
            {
                Width = 22, Height = 14, CornerRadius = new(2), Background = Brushes.Black,
                BorderBrush = AppTheme.Outline, BorderThickness = new(1),
            };
        }

        private static TextBlock Label(string resource)
        {
            return new()
            {
                [!TextBlock.TextProperty] = LiveText.Resource(resource), Foreground = AppTheme.Ink, FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        private static Slider Slider(double minimum, double maximum)
        {
            return new()
            {
                Minimum = minimum, Maximum = maximum, TickFrequency = 1, IsSnapToTickEnabled = true,
                MinHeight = 26, VerticalAlignment = VerticalAlignment.Center,
            };
        }

        private static NumericUpDown Number(decimal minimum, decimal maximum)
        {
            return new()
            {
                Minimum = minimum, Maximum = maximum, Increment = 1, FormatString = "0", Width = 56, Height = 28,
                MinHeight = 0, FontSize = 12, Padding = new(3, 0),
            };
        }

        private static Grid ValueRow(Control slider, Control number)
        {
            var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
            row.Children.Add(slider);
            Grid.SetColumn(number, 1);
            row.Children.Add(number);
            return row;
        }

        private static Grid ModeRow(Control choice, Control color)
        {
            var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
            row.Children.Add(choice);
            Grid.SetColumn(color, 1);
            row.Children.Add(color);
            return row;
        }

        private static AppearanceColorPicker ColorPicker()
        {
            var picker = new AppearanceColorPicker
            {
                Width = 38, Height = 26, MinWidth = 0, MinHeight = 0, Padding = new(0), IsAlphaEnabled = true,
                IsAlphaVisible = true, IsColorPaletteVisible = false, IsHexInputVisible = true,
            };
            picker.Styles.Add(new AnnotationColorPickerStyles());
            return picker;
        }
    }
}

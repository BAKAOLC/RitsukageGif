using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AppearanceColorDialog : Window
    {
        private readonly AppearanceColorState _pickerState = AppearanceColorState.Current;
        private readonly bool _alpha;
        private readonly bool _classic;
        private readonly Action<Color>? _preview;
        private readonly List<Action> _refresh = [];
        private Color _color;
        private HsvColor _hsv;
        private bool _hueEndpoint;
        private bool _updating;

        internal AppearanceColorDialog(AppearanceDialogStyle style, Color color, bool alpha, Action<Color>? preview)
        {
            _alpha = alpha;
            _classic = style == AppearanceDialogStyle.System;
            _preview = preview;
            _color = alpha ? color : Color.FromRgb(color.R, color.G, color.B);
            _hsv = _color.ToHsv();
            Title = UiText.Get("AppearanceSelectColor");
            Topmost = true;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            Background = AppTheme.Surface;
            var root = new StackPanel { Margin = new(_classic ? 9 : 10), Spacing = 8 };
            if (!_classic) root.Width = 240;
            root.Children.Add(_classic ? ClassicContent() : ModernContent());
            var accept = new Button
            {
                Name = "AppearanceColorConfirm", IsDefault = true,
                [!ContentProperty] = LiveText.Resource(_classic ? "PinnedSelectionConfirm" : "AnnotationNumberConfirm"),
            };
            accept.Click += (_, _) => Close(_color);
            var cancel = new Button
            {
                Name = "AppearanceColorCancel", IsCancel = true, [!ContentProperty] = LiveText.Resource("Cancel"),
            };
            cancel.Click += (_, _) => Close(null);
            root.Children.Add(new Separator());
            root.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                HorizontalAlignment = HorizontalAlignment.Right, Children = { accept, cancel },
            });
            Content = root;
            AddHandler(KeyDownEvent, HandlePickerKey, RoutingStrategies.Tunnel);
            Closed += (_, _) => StopSampling();
            RefreshControls();
        }

        internal void PreparePosition(Avalonia.PixelPoint? pointer)
        {
            if (pointer is not { } location || Screens.ScreenFromPoint(location) is not { } screen) return;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = screen.Bounds.Center;
            Opened += Center;
            return;

            void Center(object? sender, EventArgs args)
            {
                Opened -= Center;
                var size = PixelSize.FromSize(FrameSize ?? ClientSize, screen.Scaling);
                Position = screen.Bounds.CenterRect(new(size)).Position;
            }
        }

        private StackPanel ModernContent()
        {
            var spectrum = Spectrum(ColorSpectrumComponents.SaturationValue, 240, 210);
            spectrum.CornerRadius = new(8);
            var sliders = new StackPanel
            {
                Spacing = 2,
                Children = { Slider(ColorComponent.Component1, Orientation.Horizontal, 201, 13) },
            };
            if (_alpha) sliders.Children.Add(Slider(ColorComponent.Alpha, Orientation.Horizontal, 201, 13));
            var pick = CreateScreenPickButton();
            var selection = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 5, Children = { pick, sliders },
            };
            var format = new ComboBox
            {
                Name = "ModernColorFormat", ItemsSource = new[] { "Hex", "RGB", "HSV" },
                SelectedIndex = _pickerState.ModernFormat, MinWidth = 68,
            };
            var editors = new Panel { Children = { HexInput(), Components(false), Components(true) } };
            format.SelectionChanged += (_, _) =>
            {
                if (format.SelectedIndex < 0) return;
                _pickerState.SetModernFormat(format.SelectedIndex);
                SelectEditor();
            };
            SelectEditor();
            var inputs = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 2, Children = { format, editors },
            };
            if (_alpha)
                inputs.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        ModernNumber("AppearanceColorOpacity", 100, 32, () => _hsv.A * 100,
                            value => ChangeHsv(alpha: value / 100), new(0, 4, 4, 0)),
                        new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center },
                    },
                });
            var root = new StackPanel { Name = "AppearanceModernColorLayout", Spacing = 6 };
            root.Children.Add(spectrum);
            root.Children.Add(selection);
            root.Children.Add(inputs);
            return root;

            void SelectEditor()
            {
                for (var index = 0; index < editors.Children.Count; ++index)
                    editors.Children[index].IsVisible = index == _pickerState.ModernFormat;
            }
        }

        private ColorSpectrum Spectrum(ColorSpectrumComponents components, double width, double height)
        {
            var spectrum = new ColorSpectrum { Components = components, Width = width, Height = height };
            spectrum.ColorChanged += (_, _) => FromHsv(spectrum.HsvColor);
            _refresh.Add(() => spectrum.HsvColor = _hsv);
            return spectrum;
        }

        private ColorSlider Slider(ColorComponent component, Orientation orientation, double width, double height)
        {
            var slider = new ColorSlider
            {
                ColorModel = ColorModel.Hsva, ColorComponent = component, Orientation = orientation,
                Width = width, Height = height, Focusable = true,
            };
            slider.ColorChanged += (_, _) => FromHsv(slider.HsvColor);
            _refresh.Add(() => slider.HsvColor = _hsv);
            return slider;
        }

        private TextBox HexInput()
        {
            var input = new TextBox { Name = "AppearanceColorHex", Width = 90 };
            if (!_classic) input.CornerRadius = new(4, _alpha ? 0 : 4, _alpha ? 0 : 4, 4);
            input.TextChanged += (_, _) =>
            {
                if (_updating || input.Text is not { } text) return;
                var digits = text.AsSpan().Trim();
                if (digits.StartsWith("#")) digits = digits[1..];
                if (digits.Length != 6 || !uint.TryParse(digits, NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out var rgb)) return;
                var value = Color.FromUInt32(((uint)_color.A << 24) | rgb);
                if (value != _color) FromColor(value);
            };
            input.LostFocus += (_, _) => RefreshControls();
            _refresh.Add(() =>
            {
                if (!input.IsFocused) input.Text = $"#{_color.R:X2}{_color.G:X2}{_color.B:X2}";
            });
            return input;
        }

        private StackPanel Components(bool hsv)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = _classic ? 3 : 0 };
            if (hsv)
            {
                row.Children.Add(Component("AppearanceColorHue", 359, () => _hsv.H, value => ChangeHsv(value)));
                row.Children.Add(Component("AppearanceColorSaturation", 100, () => _hsv.S * 100,
                    value => ChangeHsv(saturation: value / 100)));
                row.Children.Add(Component("AppearanceColorValue", 100, () => _hsv.V * 100,
                    value => ChangeHsv(valueComponent: value / 100)));
            }
            else
            {
                row.Children.Add(Component("AppearanceColorRed", 255, () => _color.R,
                    value => FromColor(Color.FromArgb(_color.A, (byte)value, _color.G, _color.B))));
                row.Children.Add(Component("AppearanceColorGreen", 255, () => _color.G,
                    value => FromColor(Color.FromArgb(_color.A, _color.R, (byte)value, _color.B))));
                row.Children.Add(Component("AppearanceColorBlue", 255, () => _color.B,
                    value => FromColor(Color.FromArgb(_color.A, _color.R, _color.G, (byte)value))));
            }

            return row;

            Control Component(string key, int maximum, Func<double> read, Action<double> write)
            {
                if (_classic) return Field(key, maximum, 52, read, write);
                var corners = row.Children.Count == 0 ? new(4, 0, 0, 4) :
                    row.Children.Count == 2 && !_alpha ? new CornerRadius(0, 4, 4, 0) : default;
                return key == "AppearanceColorHue"
                    ? ModernNumber(key, 360, 42, () => _hueEndpoint ? 360 : read(), write, corners)
                    : ModernNumber(key, maximum, 42, read, write, corners);
            }
        }

        private NumericUpDown ModernNumber(string label, int maximum, double width, Func<double> read,
            Action<double> write, CornerRadius corners)
        {
            var input = new NumericUpDown
            {
                Minimum = 0, Maximum = maximum, Width = width, MinWidth = 0, FormatString = "0",
                ShowButtonSpinner = false, Padding = new(3, 1), CornerRadius = corners,
            };
            input.ValueChanged += (_, _) =>
            {
                if (!_updating && input.Value is { } value) write((double)value);
            };
            _refresh.Add(() => input.Value = (decimal)Math.Clamp(Math.Round(read()), 0, maximum));
            AutomationProperties.SetName(input, UiText.Get(label));
            return input;
        }

        private StackPanel Field(string label, int maximum, double width, Func<double> read, Action<double> write)
        {
            var input = new CompactNumberInput { Minimum = 0, Maximum = maximum, Width = width };
            input.ValueChanged += (_, _) =>
            {
                if (!_updating) write((double)input.Value.GetValueOrDefault());
            };
            _refresh.Add(() => input.Value = (decimal)Math.Clamp(Math.Round(read()), 0, maximum));
            AutomationProperties.SetName(input, UiText.Get(label));
            return new() { Spacing = 2, Children = { Label(label), input } };
        }

        private static TextBlock Label(string key)
        {
            return new() { [!TextBlock.TextProperty] = LiveText.Resource(key) };
        }

        private void ChangeHsv(double? hue = null, double? saturation = null, double? valueComponent = null,
            double? alpha = null)
        {
            FromHsv(new(alpha ?? _hsv.A, hue ?? _hsv.H, saturation ?? _hsv.S, valueComponent ?? _hsv.V),
                hue is 360 || (hue is null && _hueEndpoint));
        }

        private void FromHsv(HsvColor hsv, bool hueEndpoint = false)
        {
            if (_updating) return;
            _hueEndpoint = hueEndpoint;
            _hsv = _alpha ? hsv : new(1, hsv.H, hsv.S, hsv.V);
            UpdateColor(_hsv.ToRgb());
        }

        private void FromColor(Color color)
        {
            if (_updating) return;
            _hueEndpoint = false;
            var value = _alpha ? color : Color.FromRgb(color.R, color.G, color.B);
            var hsv = value.ToHsv();
            _hsv = hsv.S == 0 ? new(hsv.A, _hsv.H, hsv.S, hsv.V) : hsv;
            UpdateColor(value);
        }

        private void UpdateColor(Color color)
        {
            var previous = _color;
            _color = color;
            RefreshControls();
            if (previous != color) _preview?.Invoke(color);
        }

        private void RefreshControls()
        {
            _updating = true;
            try
            {
                foreach (var refresh in _refresh) refresh();
            }
            finally
            {
                _updating = false;
            }
        }
    }
}

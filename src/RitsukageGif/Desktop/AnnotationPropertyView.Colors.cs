using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        public Control Palette()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new(4, 0, 0, 0) };
            panel.Children.Add(ColorPicker("AnnotationColor", () => _session.Color,
                color => _session.UpdateSelectedStyleAsync(color)));
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            Color[] previous = [];
            _refresh.Add(() =>
            {
                var colors = _session.ColorPalette.Count > 0
                    ? _session.ColorPalette.ToArray()
                    : [Colors.Red, Colors.Yellow, Colors.LimeGreen, Colors.DeepSkyBlue, Colors.Black, Colors.White];
                if (!previous.SequenceEqual(colors))
                {
                    previous = colors;
                    swatches.Children.Clear();
                    foreach (var color in colors.Take(6)) swatches.Children.Add(PaletteButton(color));
                }

                foreach (var button in swatches.Children.OfType<Button>()) RefreshPaletteButton(button);
            });
            panel.Children.Add(swatches);
            var more = Popup("AnnotationColor", "ToolbarChevron", () =>
            {
                var remaining = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 220 };
                Color[] shown = [];
                _refresh.Add(() =>
                {
                    var colors = _session.ColorPalette.Skip(6).ToArray();
                    if (!shown.SequenceEqual(colors))
                    {
                        shown = colors;
                        remaining.Children.Clear();
                        foreach (var color in colors) remaining.Children.Add(PaletteButton(color));
                    }

                    foreach (var button in remaining.Children.OfType<Button>()) RefreshPaletteButton(button);
                });
                return remaining;
            });
            _refresh.Add(() => more.IsVisible = _session.ColorPalette.Count > 6);
            panel.Children.Add(more);
            return panel;
        }

        private Button PaletteButton(Color color)
        {
            var button = Button("AnnotationColor");
            button.Width = 18;
            button.Margin = new(0, 2);
            button.Padding = new(0);
            button.Tag = color;
            button.Classes.Add("palette-color");
            button.Content = Swatch(color, 16, out _);
            MinimalChrome(button);
            RefreshPaletteButton(button);
            ToolTip.SetTip(button, color.ToString());
            button.Click += async (_, _) =>
            {
                if (!_disposed) await _session.UpdateSelectedStyleAsync(color);
            };
            return button;
        }

        private void RefreshPaletteButton(Button button)
        {
            button.Classes.Set("selected-color", button.Tag is Color color && color == _session.Color);
            RefreshMinimalChrome(button);
        }

        private AppearanceColorPicker ColorPicker(string key, Func<Color> read, Func<Color, Task> write)
        {
            var picker = new AppearanceColorPicker
            {
                Width = 48, Height = 28, MinHeight = 0, MinWidth = 0, Padding = new(4), Margin = new(1, 2),
                IsAlphaEnabled = true, IsAlphaVisible = true, IsColorPaletteVisible = false, IsHexInputVisible = true,
                DialogScopeProvider = _dialogScopeProvider, Foreground = AppTheme.Ink,
            };
            picker.Styles.Add(new AnnotationColorPickerStyles());
            picker.Content = Swatch(read(), 22, out var tint);
            ToolTip.SetTip(picker, UiText.Get(key));
            picker.ColorChanged += async (_, _) =>
            {
                if (!_refreshing && !_disposed) await write(picker.Color);
            };
            _refresh.Add(() =>
            {
                picker.Color = read();
                tint.Background = new SolidColorBrush(read());
            });
            return picker;
        }

        private static Grid Swatch(Color color, double width, out Border tint)
        {
            var grid = new Grid { Width = width, Height = 18 };
            var checker = new UniformGrid { Rows = 2, Columns = 4 };
            for (var index = 0; index < 8; index++)
                checker.Children.Add(new Border
                {
                    Background = (index + index / 4) % 2 == 0 ? Brushes.White : Brushes.LightGray,
                });
            grid.Children.Add(checker);
            tint = new() { Background = new SolidColorBrush(color) };
            grid.Children.Add(tint);
            return grid;
        }
    }
}

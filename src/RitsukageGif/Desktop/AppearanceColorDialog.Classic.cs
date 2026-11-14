using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AppearanceColorDialog
    {
        private const int PaletteColumns = 8;

        private static readonly Color[] BasicColors =
        [
            .. Enumerable.Range(0, 48)
                .Select(index => Color.FromRgb((byte)(index / 3 % 4 * 85), (byte)(index / 12 * 85),
                    (byte)(index % 3 * 255 / 2))),
        ];

        private int _nextCustom;

        private StackPanel ClassicContent()
        {
            var standard = Palette(BasicColors);
            var custom = Palette(_pickerState.CustomColors, true);
            var paletteChanged = false;
            Closed += (_, _) =>
            {
                if (paletteChanged) _pickerState.Save();
            };
            var saveCustom = new Button
            {
                Name = "ClassicAddCustomColor", [!ContentProperty] = LiveText.Resource("AppearanceColorAddCustom"),
            };
            saveCustom.Click += (_, _) =>
            {
                _pickerState.SetCustomColor(_nextCustom, _color);
                paletteChanged = true;
                const int customRows = AppearanceColorState.CustomColorCount / PaletteColumns;
                var position = _nextCustom % customRows * PaletteColumns + _nextCustom / customRows;
                ((Button)custom.Children[position]).Background = new SolidColorBrush(_color);
                _nextCustom = (_nextCustom + 1) % AppearanceColorState.CustomColorCount;
            };
            var palettes = new StackPanel
            {
                Spacing = 5,
                Children =
                {
                    Label("AppearanceColorBasic"), standard, CreateScreenPickButton(),
                    Label("AppearanceColorCustom"), custom, saveCustom,
                },
            };
            var spectral = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children =
                {
                    Spectrum(ColorSpectrumComponents.HueSaturation, 190, 139),
                    Slider(ColorComponent.Component3, Orientation.Vertical, 20, 139),
                },
            };
            var swatch = new Border { Height = 24, BorderBrush = AppTheme.Outline, BorderThickness = new(1) };
            _refresh.Add(() => swatch.Background = new SolidColorBrush(_color));
            var textEntry = new StackPanel { Spacing = 4 };
            textEntry.Children.Add(swatch);
            textEntry.Children.Add(Components(true));
            textEntry.Children.Add(Components(false));
            var last = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            last.Children.Add(new StackPanel { Children = { Label("AppearanceColorHtml"), HexInput() } });
            if (_alpha)
                last.Children.Add(Field("AppearanceColorAlpha", 255, 60, () => _color.A,
                    value => ChangeHsv(alpha: value / 255)));
            textEntry.Children.Add(last);
            var editors = new StackPanel { Spacing = 5, Children = { spectral, textEntry } };
            return new()
            {
                Name = "AppearanceClassicColorLayout", Orientation = Orientation.Horizontal, Spacing = 12,
                Children = { palettes, editors },
            };
        }

        private UniformGrid Palette(IReadOnlyList<Color> colors, bool custom = false)
        {
            var grid = new UniformGrid { Columns = PaletteColumns, HorizontalAlignment = HorizontalAlignment.Left };
            var rows = colors.Count / PaletteColumns;
            for (var index = 0; index < colors.Count; ++index)
            {
                var slot = index % PaletteColumns * rows + index / PaletteColumns;
                var swatch = new Button
                {
                    Width = 22, Height = 18, MinWidth = 0, MinHeight = 0, Padding = new(0),
                    Background = new SolidColorBrush(colors[slot]), BorderThickness = new(1),
                    BorderBrush = AppTheme.Outline, Margin = new(1), CornerRadius = new(1),
                };
                if (custom) swatch.GotFocus += (_, _) => _nextCustom = slot;
                swatch.Click += (_, _) => FromColor(Color.FromArgb(_color.A,
                    colors[slot].R, colors[slot].G, colors[slot].B));
                grid.Children.Add(swatch);
            }

            return grid;
        }
    }
}

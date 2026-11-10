using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private StackPanel PinnedTextColors(List<string> colors)
        {
            var palette = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            for (var index = 0; index < colors.Count; index++)
            {
                var item = index;
                var swatch = new Ellipse
                {
                    Width = 24, Height = 24, Fill = new SolidColorBrush(Color.Parse(colors[item])),
                    Stroke = AppTheme.Muted, StrokeThickness = 1,
                };
                var picker = new AppearanceColorPicker
                {
                    Name = "PinnedTextHighlightColor" + item, Color = Color.Parse(colors[item]),
                    Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new(0),
                    IsAlphaEnabled = true, IsAlphaVisible = true, IsColorPaletteVisible = false,
                    IsHexInputVisible = true, Content = swatch,
                };
                picker.Styles.Add(new TextHighlightColorPickerStyles());
                picker.Bind(ToolTip.TipProperty, LiveText.Resource("PinnedTextHighlightColors"));
                picker.ColorChanged += (_, _) =>
                {
                    colors[item] = picker.Color.ToString();
                    swatch.Fill = new SolidColorBrush(picker.Color);
                    RequestLiveApply();
                };
                palette.Children.Add(picker);
            }

            return palette;
        }
    }
}

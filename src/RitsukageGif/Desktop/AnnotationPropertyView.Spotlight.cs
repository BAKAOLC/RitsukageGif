using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        public Control SpotlightProperties()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            var shapes = new StackPanel { Orientation = Orientation.Horizontal };
            var rectangle = Toggle("AnimationRectangle", "", properties => !properties.SpotlightEllipse,
                (properties, _) => properties with { SpotlightEllipse = false }, "AnimationRectangle");
            AttachCornerRadius(rectangle, () => !_session.Properties.SpotlightEllipse);
            shapes.Children.Add(rectangle);
            shapes.Children.Add(Toggle("AnimationEllipse", "", properties => properties.SpotlightEllipse,
                (properties, _) => properties with { SpotlightEllipse = true }, "AnimationEllipse"));
            row.Children.Add(shapes);
            row.Children.Add(new TextBlock
            {
                Text = UiText.Get("ScreenshotSpotlightOpacity"), VerticalAlignment = VerticalAlignment.Center,
            });
            var opacity = new Slider
            {
                Minimum = 1, Maximum = 100, SmallChange = 1, Width = 120, Height = 28,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var percentage = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 34 };
            opacity.ValueChanged += async (_, _) =>
            {
                if (_disposed || _refreshing) return;
                await _session.UpdateSelectedPropertiesAsync(_session.Properties with
                {
                    Opacity = opacity.Value / 100,
                });
            };
            _refresh.Add(() =>
            {
                opacity.Value = _session.Properties.Opacity * 100;
                percentage.Text = Math.Round(opacity.Value).ToString(CultureInfo.CurrentCulture) + "%";
            });
            row.Children.Add(opacity);
            row.Children.Add(percentage);
            var stroke = new CheckBox
            {
                Content = UiText.Get("ScreenshotSpotlightStroke"), MinHeight = 28,
                Margin = new(2, 0), VerticalAlignment = VerticalAlignment.Center,
            };
            stroke.IsCheckedChanged += async (_, _) =>
            {
                if (_disposed || _refreshing) return;
                await _session.UpdateSelectedPropertiesAsync(_session.Properties with
                {
                    SpotlightStroke = stroke.IsChecked == true,
                });
            };
            row.Children.Add(stroke);
            var width = NumberInput("ScreenshotSpotlightStrokeWidth", 1, 50, () => _session.Thickness,
                value => _session.UpdateSelectedStyleAsync(thickness: value));
            width.PointerWheelChanged += async (_, args) =>
            {
                if (_disposed || _refreshing || args.Delta.Y == 0) return;
                args.Handled = true;
                await _session.UpdateSelectedStyleAsync(thickness:
                    Math.Clamp(_session.Thickness + Math.Sign(args.Delta.Y), 1, 50));
            };
            AttachHoverPanel(width, () =>
            {
                var content = new StackPanel { Spacing = 3 };
                content.Children.Add(new TextBlock { Text = UiText.Get("ScreenshotSpotlightStrokeWidth") });
                var slider = new Slider { Minimum = 1, Maximum = 50, SmallChange = 1, Width = 160 };
                slider.ValueChanged += async (_, _) =>
                {
                    if (_disposed || _refreshing) return;
                    await _session.UpdateSelectedStyleAsync(thickness: slider.Value);
                };
                _refresh.Add(() => slider.Value = _session.Thickness);
                content.Children.Add(slider);
                content.Children.Add(Palette());
                return content;
            });
            _refresh.Add(() =>
            {
                stroke.IsChecked = _session.Properties.SpotlightStroke;
                width.IsEnabled = _session.Properties.SpotlightStroke;
            });
            row.Children.Add(width);
            return row;
        }
    }
}

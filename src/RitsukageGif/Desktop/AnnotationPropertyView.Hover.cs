using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        private readonly List<HoverPropertyPanel> _hoverPanels = [];

        private CompactNumberInput HoverSizeInput(string key, double minimum, double maximum,
            Func<double> read, Func<double, Task> update)
        {
            var input = NumberInput(key, minimum, maximum, read, update, increment: 1);
            input.PointerWheelChanged += async (_, args) =>
            {
                if (_disposed || _refreshing || args.Delta.Y == 0) return;
                args.Handled = true;
                await update(Math.Clamp(read() + Math.Sign(args.Delta.Y) * 2, minimum, maximum));
            };
            AttachHoverPanel(input, () =>
            {
                var content = new StackPanel { Spacing = 3 };
                content.Children.Add(new TextBlock { Text = UiText.Get(key) });
                var slider = new Slider { Minimum = minimum, Maximum = maximum, SmallChange = 1, Width = 160 };
                slider.ValueChanged += async (_, _) =>
                {
                    if (!_disposed && !_refreshing) await update(Math.Round(slider.Value));
                };
                _refresh.Add(() => slider.Value = read());
                content.Children.Add(slider);
                return content;
            });
            return input;
        }

        private void AttachHoverPanel(Control target, Func<Control> create, Func<bool>? display = null)
        {
            var panel = new HoverPropertyPanel(target, create, _ => Refresh());
            _hoverPanels.Add(panel);
            if (display is not null) _refresh.Add(() => panel.IsEnabled = display());
        }

        private void AttachCornerRadius(Button rectangle, Func<bool> selected)
        {
            AttachHoverPanel(rectangle, () =>
            {
                var content = new StackPanel { Spacing = 3 };
                content.Children.Add(new TextBlock { Text = UiText.Get("AnnotationCornerRadius") });
                var slider = new Slider { Minimum = 0, Maximum = 50, SmallChange = 1, Width = 160 };
                slider.ValueChanged += async (_, _) =>
                {
                    if (_disposed || _refreshing) return;
                    await _session.UpdateSelectedPropertiesAsync(_session.Properties with
                    {
                        CornerRadius = slider.Value,
                    });
                };
                _refresh.Add(() => slider.Value = _session.Properties.CornerRadius);
                content.Children.Add(slider);
                return content;
            }, selected);
            rectangle.PointerWheelChanged += async (_, args) =>
            {
                if (_disposed || _refreshing || args.Delta.Y == 0) return;
                args.Handled = true;
                await _session.UpdateSelectedPropertiesAsync(_session.Properties with
                {
                    CornerRadius = Math.Clamp(_session.Properties.CornerRadius + Math.Sign(args.Delta.Y), 0, 50),
                });
            };
        }

        private StackPanel MagnifierShape()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
            var rectangle = Toggle("AnimationRectangle", "", properties => !properties.MagnifierEllipse,
                (properties, _) => properties with { MagnifierEllipse = false }, "AnimationRectangle");
            AttachCornerRadius(rectangle, () => !_session.Properties.MagnifierEllipse);
            row.Children.Add(rectangle);
            row.Children.Add(Toggle("AnimationEllipse", "", properties => properties.MagnifierEllipse,
                (properties, _) => properties with { MagnifierEllipse = true }, "AnimationEllipse"));
            return row;
        }
    }
}

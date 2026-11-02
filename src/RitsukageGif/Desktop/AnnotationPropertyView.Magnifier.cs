using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        public Control MagnifierProperties()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            row.Children.Add(Toggle("AnnotationMagnifierEllipse", "", p => p.MagnifierEllipse,
                (p, value) => p with { MagnifierEllipse = value }, "AnimationEllipse"));
            var corners = CornerRadius();
            row.Children.Add(corners);
            _refresh.Add(() => corners.IsVisible = !_session.Properties.MagnifierEllipse);
            row.Children.Add(PropertyNumber("AnnotationMagnification", 1, 8, p => p.Magnification,
                (p, value) => p with { Magnification = value }));
            row.Children.Add(WidthInput());
            row.Children.Add(Popup("AnnotationMagnifierOptions", "GeneralHeading", () =>
            {
                var options = new StackPanel { Spacing = 4, MaxWidth = 250 };
                var links = new ComboBox
                {
                    ItemsSource = Enum.GetValues<AnnotationMagnifierLink>()
                        .Select(value => UiText.Get("AnnotationMagnifierLink" + value)).ToArray(),
                    MinHeight = 26, MinWidth = 140,
                };
                links.SelectionChanged += async (_, _) =>
                {
                    if (_disposed || _refreshing || links.SelectedIndex < 0) return;
                    await _session.UpdateSelectedPropertiesAsync(_session.Properties with
                    {
                        MagnifierLink = (AnnotationMagnifierLink)links.SelectedIndex,
                    });
                };
                _refresh.Add(() => links.SelectedIndex = (int)_session.Properties.MagnifierLink);
                options.Children.Add(Labeled("AnnotationMagnifierLink", links));
                options.Children.Add(MagnifierCheck("AnnotationMagnifierAntialias", p => p.MagnifierAntialias,
                    (p, value) => p with { MagnifierAntialias = value }));
                options.Children.Add(MagnifierCheck("AnnotationMagnifierAnnotations", p => p.MagnifierShowAnnotations,
                    (p, value) => p with { MagnifierShowAnnotations = value }));
                options.Children.Add(MagnifierCheck("AnnotationMagnifierShadow", p => p.MagnifierShadow,
                    (p, value) => p with { MagnifierShadow = value }));
                return options;
            }));
            row.Children.Add(Palette());
            return row;
        }

        private CheckBox MagnifierCheck(string key, Func<ScreenshotAnnotationProperties, bool> read,
            Func<ScreenshotAnnotationProperties, bool, ScreenshotAnnotationProperties> write)
        {
            var control = new CheckBox { Content = UiText.Get(key), MinHeight = 26, Margin = new(0) };
            control.IsCheckedChanged += async (_, _) =>
            {
                if (_disposed || _refreshing) return;
                await _session.UpdateSelectedPropertiesAsync(write(_session.Properties, control.IsChecked == true));
            };
            _refresh.Add(() => control.IsChecked = read(_session.Properties));
            return control;
        }
    }
}

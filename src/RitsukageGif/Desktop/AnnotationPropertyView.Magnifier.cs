using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        public Control MagnifierProperties()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            row.Children.Add(MagnifierShape());
            row.Children.Add(WidthInput());
            var links = new ComboBox
            {
                ItemsSource = new[]
                {
                    UiText.Get("ScreenshotMagnifierLinkLine"), UiText.Get("AnnotationMagnifierLinkDotLine"),
                    UiText.Get("ScreenshotMagnifierLinkShape"), UiText.Get("ScreenshotMagnifierLinkHide"),
                },
                Height = 28, MinHeight = 0, Width = 65, Padding = new(4, 0), Margin = new(2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(links, UiText.Get("AnnotationMagnifierLink"));
            AutomationProperties.SetName(links, UiText.Get("AnnotationMagnifierLink"));
            links.SelectionChanged += async (_, _) =>
            {
                if (_disposed || _refreshing || links.SelectedIndex < 0) return;
                await _session.UpdateSelectedPropertiesAsync(_session.Properties with
                {
                    MagnifierLink = (AnnotationMagnifierLink)links.SelectedIndex,
                });
            };
            _refresh.Add(() => links.SelectedIndex = (int)_session.Properties.MagnifierLink);
            row.Children.Add(links);
            row.Children.Add(PropertyNumber("AnnotationMagnification", 1, 8, properties => properties.Magnification,
                (properties, value) => properties with { Magnification = value }));
            row.Children.Add(PropertyCheck("ScreenshotMagnifierEraseMark",
                properties => !properties.MagnifierShowAnnotations,
                (properties, value) => properties with { MagnifierShowAnnotations = !value }));
            row.Children.Add(PropertyCheck("AnnotationMagnifierAntialias", properties => properties.MagnifierAntialias,
                (properties, value) => properties with { MagnifierAntialias = value }));
            row.Children.Add(PropertyCheck("AnnotationMagnifierShadow", properties => properties.MagnifierShadow,
                (properties, value) => properties with { MagnifierShadow = value }));
            row.Children.Add(Palette());
            return row;
        }

        private CheckBox PropertyCheck(string key, Func<ScreenshotAnnotationProperties, bool> read,
            Func<ScreenshotAnnotationProperties, bool, ScreenshotAnnotationProperties> write)
        {
            var control = new CheckBox
            {
                Content = UiText.Get(key), MinHeight = 28, Margin = new(2, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
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

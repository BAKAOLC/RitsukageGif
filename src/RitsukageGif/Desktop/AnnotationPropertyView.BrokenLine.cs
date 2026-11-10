using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        public Control BrokenLineProperties()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            row.Children.Add(Curved());
            row.Children.Add(StrokeStyle());
            row.Children.Add(WidthInput());
            var arrows = new CheckBox
            {
                Content = UiText.Get("ScreenshotArrow"), MinHeight = 28, Margin = new(2, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            arrows.AttachedToVisualTree += (_, _) => _session.TrackPolylineArrowTarget(arrows, true);
            arrows.DetachedFromVisualTree += (_, _) => _session.TrackPolylineArrowTarget(arrows, false);
            arrows.IsCheckedChanged += async (_, _) =>
            {
                if (!_disposed && !_refreshing)
                    await _session.SetPolylineArrowsAsync(arrows.IsChecked == true);
            };
            var start = Endpoint(true);
            var end = Endpoint(false);
            row.Children.Add(arrows);
            row.Children.Add(start);
            row.Children.Add(end);
            row.Children.Add(Palette());
            _refresh.Add(() =>
            {
                var properties = _session.Properties;
                var active = HasArrows(properties);
                arrows.IsChecked = active;
                var filled = properties.StrokeStyle == AnnotationStrokeStyle.Filled;
                arrows.IsEnabled = _session.CanSetPolylineArrows;
                start.IsEnabled = end.IsEnabled = active && !filled;
            });
            return row;

            static bool HasArrows(ScreenshotAnnotationProperties properties)
            {
                return properties.StartEndpoint != AnnotationEndpointStyle.None ||
                       properties.EndEndpoint != AnnotationEndpointStyle.None;
            }
        }
    }
}

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        public Control GeometryShape()
        {
            var choices = new StackPanel { Orientation = Orientation.Horizontal };
            choices.Children.Add(Toggle("AnimationRectangle", "",
                properties => properties.GeometryShape == AnnotationGeometryShape.Rectangle,
                (properties, _) => properties with { GeometryShape = AnnotationGeometryShape.Rectangle },
                "AnimationRectangle"));
            choices.Children.Add(Toggle("AnimationEllipse", "",
                properties => properties.GeometryShape == AnnotationGeometryShape.Ellipse,
                (properties, _) => properties with { GeometryShape = AnnotationGeometryShape.Ellipse },
                "AnimationEllipse"));
            return choices;
        }

        public Control CornerRadius()
        {
            var input = HoverSizeInput("AnnotationCornerRadius", 0, 90, () => _session.Properties.CornerRadius,
                value => _session.UpdateSelectedPropertiesAsync(_session.Properties with { CornerRadius = value }));
            _refresh.Add(() =>
                input.IsEnabled = _session.Properties.GeometryShape == AnnotationGeometryShape.Rectangle);
            return input;
        }

        private StackPanel Curved()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var curved in new[] { false, true })
            {
                var button = Toggle(curved ? "AnnotationCurved" : "ScreenshotAnnotationBrokenLine", "",
                    properties => properties.Curved == curved,
                    (properties, _) => properties with { Curved = curved });
                var preview = new AnnotationCurvePreview { Curved = curved };
                button.Content = preview;
                _refresh.Add(() =>
                {
                    preview.Stroke = button.Foreground ?? AppTheme.Ink;
                    preview.InvalidateVisual();
                });
                row.Children.Add(button);
            }

            return row;
        }

        private Button Endpoint(bool start)
        {
            var key = start ? "AnnotationStartEndpoint" : "AnnotationEndEndpoint";
            var button = Popup(key, "ToolbarChevron", () =>
            {
                var choices = new WrapPanel { MaxWidth = 160, Orientation = Orientation.Horizontal };
                foreach (var style in Enum.GetValues<AnnotationEndpointStyle>())
                {
                    var choice = Button("AnnotationEndpoint" + style);
                    choice.Width = 46;
                    choice.Height = 30;
                    choice.Content = new AnnotationEndpointPreview { Style = style, Start = start };
                    choice.Click += async (_, _) =>
                    {
                        if (_disposed) return;
                        foreach (var flyout in _flyouts) flyout.Hide();
                        var properties = _session.Properties;
                        await _session.UpdateSelectedPropertiesAsync(start
                            ? properties with { StartEndpoint = style }
                            : properties with { EndEndpoint = style });
                    };
                    _refresh.Add(() =>
                    {
                        var selected = (start ? _session.Properties.StartEndpoint : _session.Properties.EndEndpoint) ==
                                       style;
                        choice.Background = selected ? AppTheme.Surface : Brushes.Transparent;
                        choice.BorderBrush = selected ? AppTheme.Accent : Brushes.Transparent;
                    });
                    choices.Children.Add(choice);
                }

                return choices;
            });
            button.Width = 60;
            var preview = new AnnotationEndpointPreview { Start = start };
            button.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center,
                Children = { preview, DropDownChevron.Create(button) },
            };
            _refresh.Add(() =>
                preview.Style = start ? _session.Properties.StartEndpoint : _session.Properties.EndEndpoint);
            return button;
        }
    }
}

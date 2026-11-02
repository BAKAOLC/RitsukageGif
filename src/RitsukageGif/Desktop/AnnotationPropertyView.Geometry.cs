using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        public Control GeometryShape()
        {
            return Toggle("AnimationEllipse", "", p => p.GeometryShape == AnnotationGeometryShape.Ellipse,
                (p, value) => p with
                {
                    GeometryShape = value ? AnnotationGeometryShape.Ellipse : AnnotationGeometryShape.Rectangle,
                }, "AnimationEllipse");
        }

        public Control CornerRadius()
        {
            return Popup("AnnotationCornerRadius", "AnimationRectangle", () =>
                Labeled("AnnotationCornerRadius", PropertyNumber("AnnotationCornerRadius", 0, 1024,
                    p => p.CornerRadius, (p, value) => p with { CornerRadius = value })));
        }

        public Control Curved()
        {
            var button = Toggle("AnnotationCurved", "", p => p.Curved,
                (p, value) => p with { Curved = value });
            var preview = new AnnotationCurvePreview();
            button.Content = preview;
            _refresh.Add(() =>
            {
                preview.Curved = _session.Properties.Curved;
                preview.Stroke = button.Foreground ?? AppTheme.Ink;
                preview.InvalidateVisual();
            });
            return button;
        }

        public Control SpotlightShape()
        {
            return Toggle("AnnotationSpotlightEllipse", "○", p => p.SpotlightEllipse,
                (p, value) => p with { SpotlightEllipse = value }, "AnimationEllipse");
        }

        public Control SpotlightStroke()
        {
            return Toggle("AnnotationSpotlightStroke", "□", p => p.SpotlightStroke,
                (p, value) => p with { SpotlightStroke = value }, "AnimationRectangle");
        }

        public Control Endpoint(bool start)
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

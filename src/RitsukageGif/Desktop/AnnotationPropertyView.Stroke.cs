using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        public Control StrokeStyle()
        {
            var values = Enum.GetValues<AnnotationStrokeStyle>()
                .Where(style => style != AnnotationStrokeStyle.Filled ||
                                ScreenshotToolCatalog.Get(_tool).ObjectKind == "BrokenLine").ToArray();

            var button = Popup("AnnotationStrokeStyle", "ToolbarChevron", () =>
            {
                var choices = new WrapPanel { MaxWidth = 204, Orientation = Orientation.Horizontal };
                foreach (var style in values)
                {
                    var choice = Button("AnnotationStroke" + style);
                    choice.Width = 66;
                    choice.Height = 30;
                    choice.Content = new AnnotationStrokePreview { Style = style };
                    choice.Click += async (_, _) =>
                    {
                        if (_disposed) return;
                        foreach (var flyout in _flyouts) flyout.Hide();
                        await _session.UpdateSelectedPropertiesAsync(_session.Properties with { StrokeStyle = style });
                    };
                    _refresh.Add(() =>
                    {
                        var selected = _session.Properties.StrokeStyle == style;
                        choice.Background = selected ? AppTheme.Surface : Brushes.Transparent;
                        choice.BorderBrush = selected ? AppTheme.Accent : Brushes.Transparent;
                    });
                    choices.Children.Add(choice);
                }

                BindChoiceWheel(choices, values, () => _session.Properties.StrokeStyle, Update);
                return choices;
            });
            var preview = new AnnotationStrokePreview { Style = _session.Properties.StrokeStyle };
            button.Width = 72;
            button.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { preview, DropDownChevron.Create(button) },
            };
            _refresh.Add(() => preview.Style = _session.Properties.StrokeStyle);
            BindChoiceWheel(button, values, () => _session.Properties.StrokeStyle, Update);
            return button;

            Task Update(AnnotationStrokeStyle style)
            {
                return _session.UpdateSelectedPropertiesAsync(_session.Properties with { StrokeStyle = style });
            }
        }
    }
}

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        public Control ArrowShape()
        {
            var values = Enum.GetValues<AnnotationArrowShape>();

            var button = Popup("AnnotationArrowShape", "ToolbarChevron", () =>
            {
                var choices = new WrapPanel { MaxWidth = 210, Orientation = Orientation.Horizontal };
                foreach (var shape in values)
                {
                    var choice = Button("AnnotationArrow" + shape);
                    choice.Width = 68;
                    choice.Height = 30;
                    choice.Content = new AnnotationArrowPreview { Shape = shape };
                    choice.Click += async (_, _) =>
                    {
                        if (_disposed) return;
                        foreach (var flyout in _flyouts) flyout.Hide();
                        await _session.UpdateSelectedPropertiesAsync(_session.Properties with { ArrowShape = shape });
                    };
                    _refresh.Add(() =>
                    {
                        var selected = _session.Properties.ArrowShape == shape;
                        choice.Background = selected ? AppTheme.Surface : Brushes.Transparent;
                        choice.BorderBrush = selected ? AppTheme.Accent : Brushes.Transparent;
                    });
                    choices.Children.Add(choice);
                }

                BindChoiceWheel(choices, values, () => _session.Properties.ArrowShape, Update);
                return choices;
            });
            var preview = new AnnotationArrowPreview { Shape = _session.Properties.ArrowShape };
            button.Width = 78;
            button.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { preview, DropDownChevron.Create(button) },
            };
            _refresh.Add(() => preview.Shape = _session.Properties.ArrowShape);
            BindChoiceWheel(button, values, () => _session.Properties.ArrowShape, Update);
            return button;

            Task Update(AnnotationArrowShape shape)
            {
                return _session.UpdateSelectedPropertiesAsync(_session.Properties with { ArrowShape = shape });
            }
        }
    }
}

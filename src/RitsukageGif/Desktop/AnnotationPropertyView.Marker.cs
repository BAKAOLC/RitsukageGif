using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        public Control MarkerProperties()
        {
            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            controls.Children.Add(Toggle("AnnotationMarkerRectangle", "",
                p => p.MarkerPath == AnnotationMarkerPath.Rectangle,
                (p, _) => p with { MarkerPath = AnnotationMarkerPath.Rectangle }, "AnimationRectangle"));
            controls.Children.Add(Toggle("AnnotationMarkerLine", "", p => p.MarkerPath == AnnotationMarkerPath.Line,
                (p, _) => p with { MarkerPath = AnnotationMarkerPath.Line }, "AnnotationToolLine"));
            var width = WidthInput();
            controls.Children.Add(width);
            var blend = new AnnotationChoiceComboBox
            {
                MinWidth = 92, MinHeight = 28, VerticalAlignment = VerticalAlignment.Center,
                ItemsSource = new[]
                    { UiText.Get("AnnotationMarkerMultiply"), UiText.Get("AnnotationMarkerTranslucent") },
            };
            ToolTip.SetTip(blend, UiText.Get("AnnotationMarkerBlend"));
            blend.SelectionChanged += async (_, _) =>
            {
                if (_refreshing || blend.SelectedIndex < 0) return;
                await _session.UpdateSelectedPropertiesAsync(_session.Properties with
                {
                    MarkerBlend = (AnnotationMarkerBlend)blend.SelectedIndex,
                });
            };
            controls.Children.Add(blend);
            controls.Children.Add(Palette());
            _refresh.Add(() =>
            {
                var properties = _session.Properties;
                blend.SelectedIndex = (int)properties.MarkerBlend;
                width.IsEnabled = properties.MarkerPath == AnnotationMarkerPath.Line;
            });
            return controls;
        }
    }
}

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        public Control PixelToolProperties()
        {
            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            controls.Children.Add(PathButton(AnnotationPixelPath.Rectangle, "AnnotationPixelRectangle",
                "AnimationRectangle"));
            controls.Children.Add(PathButton(AnnotationPixelPath.Brush, "AnnotationPixelBrush", "AnimationBrush"));
            var width = WidthInput();
            controls.Children.Add(width);
            var corners = CornerRadius();
            controls.Children.Add(corners);
            _refresh.Add(() =>
            {
                width.IsVisible = _session.Properties.PixelPath == AnnotationPixelPath.Brush;
                corners.IsVisible = _session.Properties.PixelPath == AnnotationPixelPath.Rectangle;
            });
            if (_tool is ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.SmartErase)
            {
                controls.Children.Add(PixelModeButton(ScreenshotTool.Mosaic));
                controls.Children.Add(PixelModeButton(ScreenshotTool.Blur));
                controls.Children.Add(PixelModeButton(ScreenshotTool.SmartErase));
                var mosaic = Mosaic();
                var blur = Blur();
                var inpainting = PropertyNumber("AnnotationInpaintingRadius", 1, 32, p => p.InpaintingRadius,
                    (p, value) => p with { InpaintingRadius = (int)value }, true);
                controls.Children.Add(mosaic);
                controls.Children.Add(blur);
                controls.Children.Add(inpainting);
                _refresh.Add(() =>
                {
                    var mode = _session.SelectedTool ?? _session.Tool;
                    mosaic.IsVisible = mode == ScreenshotTool.Mosaic;
                    blur.IsVisible = mode == ScreenshotTool.Blur;
                    inpainting.IsVisible = mode == ScreenshotTool.SmartErase;
                });
            }
            else
            {
                var clear = Button("AnnotationClearAll", icon: "AnnotationClearAll");
                clear.Click += (_, _) => _session.ClearAnnotations();
                controls.Children.Add(clear);
            }

            return controls;
        }

        private Button PathButton(AnnotationPixelPath path, string key, string icon)
        {
            return Toggle(key, "", p => p.PixelPath == path,
                (p, _) => p with { PixelPath = path }, icon);
        }

        private Button PixelModeButton(ScreenshotTool tool)
        {
            var definition = ScreenshotToolCatalog.Get(tool);
            var button = Button(definition.NameKey, icon: definition.IconKey);
            button.Click += async (_, _) =>
            {
                if (!_disposed && !_refreshing) await _session.SetPixelEffectModeAsync(tool);
            };
            _refresh.Add(() =>
            {
                var active = (_session.SelectedTool ?? _session.Tool) == tool;
                button.Classes.Set("active-tool", active);
                button.Background = active ? AppTheme.Accent : Brushes.Transparent;
                button.Foreground = active ? Brushes.White : AppTheme.Ink;
            });
            return button;
        }
    }
}

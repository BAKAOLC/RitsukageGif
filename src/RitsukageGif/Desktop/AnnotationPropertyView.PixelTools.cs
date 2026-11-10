using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        private double? _pendingPixelStrength;
        private bool _writingPixelStrength;

        public Control PixelToolProperties()
        {
            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            controls.Children.Add(PathButton(AnnotationPixelPath.Rectangle, "AnimationRectangle",
                "AnimationRectangle"));
            controls.Children.Add(PathButton(AnnotationPixelPath.Brush, "ScreenshotAnnotationPencil",
                "AnimationBrush"));
            var width = WidthInput();
            controls.Children.Add(width);
            _refresh.Add(() => { width.IsEnabled = _session.Properties.PixelPath == AnnotationPixelPath.Brush; });
            if (_tool is ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.SmartErase)
            {
                AddPixelEffectControls(controls, false);
            }
            else
            {
                var clear = Button("ScreenshotAnnotationEraserClearAll", icon: "AnnotationClearAll");
                ToolTip.SetTip(clear, UiText.Get("ScreenshotAnnotationEraserClearAllTip"));
                clear.Click += (_, _) => _session.ClearAnnotations();
                controls.Children.Add(clear);
            }

            return controls;
        }

        public Control AutoMosaicProperties()
        {
            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            AddPixelEffectControls(controls, true);
            var sync = new CheckBox
            {
                Content = UiText.Get("ScreenshotAnnotationSynchronous"), MinHeight = 28, Margin = new(4, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            sync.IsCheckedChanged += async (_, _) =>
            {
                if (!_disposed && !_refreshing)
                    await _session.UpdateSelectedPropertiesAsync(_session.Properties with
                    {
                        AutoMosaicSynchronous = sync.IsChecked == true,
                    });
            };
            _refresh.Add(() => sync.IsChecked = _session.Properties.AutoMosaicSynchronous);
            controls.Children.Add(sync);
            return controls;
        }

        private void AddPixelEffectControls(Panel controls, bool automatic)
        {
            var mode = new AnnotationChoiceComboBox
            {
                MinWidth = 98, Height = 28, MinHeight = 0, Margin = new(2),
                VerticalContentAlignment = VerticalAlignment.Center,
                ItemsSource = automatic
                    ? new[] { UiText.Get("AnimationMosaic"), UiText.Get("AnimationBlur") }
                    : new[]
                    {
                        UiText.Get("AnimationMosaic"), UiText.Get("AnimationBlur"), UiText.Get("AnnotationSmartErase"),
                    },
            };
            ToolTip.SetTip(mode, UiText.Get("ScreenshotAnnotationMosaicMode"));
            mode.SelectionChanged += async (_, _) =>
            {
                if (_disposed || _refreshing || mode.SelectedIndex < 0) return;
                await _session.SetPixelEffectModeAsync(mode.SelectedIndex switch
                {
                    1 => ScreenshotTool.Blur,
                    2 => ScreenshotTool.SmartErase,
                    _ => ScreenshotTool.Mosaic,
                });
            };
            var strength = new Slider
            {
                Minimum = 2, Maximum = 28, TickFrequency = 1, IsSnapToTickEnabled = true,
                Width = 120, Height = 28, Margin = new(4, 0), VerticalAlignment = VerticalAlignment.Center,
            };
            strength.PropertyChanged += (sender, args) =>
            {
                if (_disposed || _refreshing || args.Property != RangeBase.ValueProperty) return;
                _pendingPixelStrength = strength.Value;
                if (!_writingPixelStrength) _ = WritePixelStrengthAsync();
            };
            _refresh.Add(() =>
            {
                var current = _session.SelectedTool == ScreenshotTool.AutoMosaic ||
                              _session.Tool == ScreenshotTool.AutoMosaic
                    ? (int)_session.Properties.MosaicMode
                    : (_session.SelectedTool ?? _session.Tool) switch
                    {
                        ScreenshotTool.Blur => 1,
                        ScreenshotTool.SmartErase => 2,
                        _ => 0,
                    };
                mode.SelectedIndex = current;
                strength.IsVisible = current != 2;
                strength.Minimum = current == 1 ? 3 : 2;
                strength.Maximum = current == 1 ? 50 : 28;
                strength.Value = current switch
                {
                    1 => _session.Properties.BlurRadius,
                    2 => _session.Properties.InpaintingRadius,
                    _ => _session.MosaicSize,
                };
                ToolTip.SetTip(strength, strength.Value.ToString(CultureInfo.CurrentCulture));
            });
            controls.Children.Add(mode);
            controls.Children.Add(strength);
        }

        private async Task WritePixelStrengthAsync()
        {
            _writingPixelStrength = true;
            try
            {
                while (!_disposed && _pendingPixelStrength is { } value)
                {
                    _pendingPixelStrength = null;
                    var tool = _session.SelectedTool == ScreenshotTool.AutoMosaic ||
                               _session.Tool == ScreenshotTool.AutoMosaic
                        ? _session.Properties.MosaicMode switch
                        {
                            AnnotationMosaicMode.Blur => ScreenshotTool.Blur,
                            _ => ScreenshotTool.Mosaic,
                        }
                        : _session.SelectedTool ?? _session.Tool;
                    if (tool == ScreenshotTool.Mosaic)
                        await _session.UpdateSelectedStyleAsync(mosaicSize: (int)value);
                    else
                        await _session.UpdateSelectedPropertiesAsync(tool == ScreenshotTool.Blur
                            ? _session.Properties with { BlurRadius = (int)value }
                            : _session.Properties with { InpaintingRadius = (int)value });
                }
            }
            finally
            {
                _writingPixelStrength = false;
            }
        }

        private Button PathButton(AnnotationPixelPath path, string key, string icon)
        {
            return Toggle(key, "", p => p.PixelPath == path, (p, _) => p with { PixelPath = path }, icon);
        }
    }
}

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        public Control NumberProperties()
        {
            var panel = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 640 };
            panel.Children.Add(NumberStyleChooser());
            panel.Children.Add(NextNumber());
            panel.Children.Add(NumberFormatChooser());
            panel.Children.Add(HoverSizeInput("AnnotationNumberSize", 1, _session.MaximumBrushThickness,
                () => _session.Thickness, value => _session.UpdateSelectedStyleAsync(thickness: value)));
            panel.Children.Add(Palette());
            var caption = NumberCaptionProperties();
            var leader = NumberLeaderChooser();
            caption.IsVisible = leader.IsVisible = false;
            panel.Children.Add(caption);
            panel.Children.Add(leader);
            var advanced = Button("AnnotationNumberAdvanced");
            var chevron = DropDownChevron.Create(advanced);
            advanced.Content = chevron;
            advanced.Click += (_, _) =>
            {
                if (_disposed) return;
                caption.IsVisible = leader.IsVisible = !caption.IsVisible;
                chevron.RenderTransform = new RotateTransform(caption.IsVisible ? 180 : 0);
            };
            panel.Children.Add(advanced);
            return panel;
        }

        private AnnotationChoiceComboBox NumberFormatChooser()
        {
            var combo = new AnnotationChoiceComboBox
            {
                Width = 94, Height = 28, MinHeight = 0, Margin = new(2), Padding = new(6, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                ItemsSource = new[] { "1.2.3", "I.II.III", "a.b.c", "A.B.C", "一.二.三" },
            };
            ToolTip.SetTip(combo, UiText.Get("AnnotationNumberFormat"));
            combo.SelectionChanged += async (_, _) =>
            {
                if (!_refreshing && !_disposed && combo.SelectedIndex >= 0)
                    await _session.UpdateSelectedPropertiesAsync(_session.Properties with
                    {
                        NumberFormat = (AnnotationNumberFormat)combo.SelectedIndex,
                    });
            };
            _refresh.Add(() => combo.SelectedIndex = (int)_session.Properties.NumberFormat);
            return combo;
        }

        private Button NumberStyleChooser()
        {
            var button = Popup("AnnotationNumberStyle", "AnnotationToolNumber", () =>
            {
                var content = new StackPanel { Spacing = 4 };
                var styles = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var style in Enum.GetValues<AnnotationNumberStyle>())
                {
                    var choice = Button("AnnotationNumberStyle" + style);
                    choice.Content = new AnnotationNumberStylePreview { Style = style, Ink = _session.Color };
                    choice.Click += async (_, _) =>
                    {
                        if (_disposed) return;
                        foreach (var flyout in _flyouts) flyout.Hide();
                        await _session.UpdateSelectedPropertiesAsync(_session.Properties with { NumberStyle = style });
                    };
                    _refresh.Add(() =>
                    {
                        choice.BorderBrush = _session.Properties.NumberStyle == style
                            ? AppTheme.Accent
                            : Brushes.Transparent;
                        var preview = (AnnotationNumberStylePreview)choice.Content!;
                        preview.Ink = _session.Color;
                        preview.TextColor = _session.NumberForeground(_session.Color);
                    });
                    styles.Children.Add(choice);
                }

                content.Children.Add(styles);
                return content;
            });
            button.Width = 46;
            var current = new AnnotationNumberStylePreview();
            button.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 2,
                Children = { current, DropDownChevron.Create(button) },
            };
            _refresh.Add(() =>
            {
                current.Style = _session.Properties.NumberStyle;
                current.Ink = _session.Color;
                current.TextColor = _session.NumberForeground(_session.Color);
            });
            return button;
        }

        private Button NumberLeaderChooser()
        {
            var button = Popup("AnnotationNumberLeader", "ScreenshotArrow", () =>
            {
                var choices = new WrapPanel { MaxWidth = 220 };
                foreach (var style in Enum.GetValues<AnnotationEndpointStyle>())
                {
                    var choice = Button("AnnotationEndpoint" + style);
                    choice.Width = 42;
                    choice.Content = new AnnotationNumberLeaderPreview { Style = style };
                    choice.Click += async (_, _) =>
                    {
                        if (_disposed) return;
                        foreach (var flyout in _flyouts) flyout.Hide();
                        await _session.UpdateSelectedPropertiesAsync(_session.Properties with
                        {
                            EndEndpoint = style, NumberCallout = false,
                        });
                    };
                    choices.Children.Add(choice);
                }

                var callout = Button("AnnotationNumberCallout");
                callout.Width = 42;
                callout.Content = new AnnotationNumberLeaderPreview { Callout = true };
                callout.Click += async (_, _) =>
                {
                    if (_disposed) return;
                    foreach (var flyout in _flyouts) flyout.Hide();
                    await _session.UpdateSelectedPropertiesAsync(_session.Properties with { NumberCallout = true });
                };
                choices.Children.Add(callout);
                return choices;
            });
            button.Width = 54;
            var preview = new AnnotationNumberLeaderPreview();
            button.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 2,
                Children = { preview, DropDownChevron.Create(button) },
            };
            _refresh.Add(() =>
            {
                preview.Style = _session.Properties.EndEndpoint;
                preview.Callout = _session.Properties.NumberCallout;
                preview.InvalidateVisual();
            });
            return button;
        }

        private StackPanel NumberCaptionProperties()
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            content.Children.Add(Font());
            content.Children.Add(Toggle("AnnotationNumberCaptionOutline", "A", p => p.NumberCaptionOutline,
                (p, value) => p with { NumberCaptionOutline = value }));
            content.Children.Add(Toggle("AnnotationNumberCaptionGuide", "—", p => p.NumberCaptionGuide,
                (p, value) => p with { NumberCaptionGuide = value }));
            double[] scales = [.2, .4, .6, .8, 1];
            var scale = new AnnotationChoiceComboBox
            {
                Width = 100, Height = 28, MinHeight = 0, Margin = new(2), Padding = new(6, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                ItemsSource = new[]
                {
                    UiText.Get("AnnotationSizeSmallest"), UiText.Get("AnnotationSizeSmall"),
                    UiText.Get("AnnotationSizeMedium"), UiText.Get("AnnotationSizeLarge"),
                    UiText.Get("AnnotationSizeLargest"),
                },
            };
            ToolTip.SetTip(scale, UiText.Get("AnimationFontSize"));
            scale.SelectionChanged += async (_, _) =>
            {
                if (!_disposed && !_refreshing && scale.SelectedIndex >= 0)
                    await _session.UpdateSelectedPropertiesAsync(_session.Properties with
                    {
                        NumberCaptionScale = scales[scale.SelectedIndex],
                    });
            };
            _refresh.Add(() => scale.SelectedIndex = Array.FindIndex(scales,
                value => Math.Abs(value - _session.Properties.NumberCaptionScale) < .001));
            content.Children.Add(scale);
            return content;
        }
    }
}

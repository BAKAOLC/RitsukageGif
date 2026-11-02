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
            panel.Children.Add(NumberInput("AnnotationNumberSize", 1.7, 1740.8,
                () => _session.FontSize * 1.7, value => _session.UpdateSelectedStyleAsync(fontSize: value / 1.7)));
            panel.Children.Add(NumberLeaderChooser());
            panel.Children.Add(NumberCaptionProperties());
            panel.Children.Add(Palette());
            return panel;
        }

        private ComboBox NumberFormatChooser()
        {
            var combo = new ComboBox
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
                        preview.InvalidateVisual();
                    });
                    styles.Children.Add(choice);
                }

                content.Children.Add(styles);
                content.Children.Add(Toggle("AnnotationNumberAutomaticOutline", "A", p => p.NumberAutomaticOutline,
                    (p, value) => p with { NumberAutomaticOutline = value }));
                var picker = ColorPicker("AnnotationNumberOutlineColor", () => _session.Properties.NumberOutlineColor,
                    color => _session.UpdateSelectedPropertiesAsync(_session.Properties with
                    {
                        NumberOutlineColor = color,
                    }));
                _refresh.Add(() => picker.IsEnabled = !_session.Properties.NumberAutomaticOutline);
                content.Children.Add(Labeled("AnnotationNumberOutlineColor", picker));
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
                current.InvalidateVisual();
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

        private Button NumberCaptionProperties()
        {
            return Popup("AnnotationNumberCaption", "AnimationText", () =>
            {
                var content = new StackPanel { Spacing = 4 };
                var edit = Button("AnnotationNumberCaption", icon: "AnimationText");
                edit.Click += (_, _) => ShowTextEditor(edit);
                _refresh.Add(() => edit.IsEnabled = _session.SelectedTool == ScreenshotTool.Number);
                content.Children.Add(edit);
                content.Children.Add(Font());
                var styles = new StackPanel { Orientation = Orientation.Horizontal };
                styles.Children.Add(Toggle("AnimationBold", "B", p => p.Bold, (p, value) => p with { Bold = value }));
                styles.Children.Add(Toggle("AnimationItalic", "I", p => p.Italic,
                    (p, value) => p with { Italic = value }));
                content.Children.Add(styles);
                content.Children.Add(Labeled("AnnotationNumberCaptionScale",
                    PropertyNumber("AnnotationNumberCaptionScale", 25, 400, p => p.NumberCaptionScale * 100,
                        (p, value) => p with { NumberCaptionScale = value / 100 }, true)));
                content.Children.Add(Toggle("AnnotationNumberCaptionGuide", "—", p => p.NumberCaptionGuide,
                    (p, value) => p with { NumberCaptionGuide = value }));
                content.Children.Add(Toggle("AnnotationNumberCaptionOutline", "A", p => p.NumberCaptionOutline,
                    (p, value) => p with { NumberCaptionOutline = value }));
                return content;
            });
        }
    }
}

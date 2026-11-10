using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        public Control WatermarkProperties()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            var input = new TextBox
            {
                IsReadOnly = true, PlaceholderText = UiText.Get("AnnotationWatermarkInput"),
                Width = 150, Height = 28, MinHeight = 0, Padding = new(5, 2),
                VerticalContentAlignment = VerticalAlignment.Center, Cursor = new(StandardCursorType.Hand),
            };
            var clear = Button("AnnotationWatermarkClear", icon: "Cancel");
            clear.Width = 22;
            clear.Height = 24;
            clear.Margin = new(0);
            MinimalChrome(clear);
            input.InnerRightContent = clear;
            clear.Click += (_, _) =>
            {
                if (_disposed || !_session.BeginTextPropertyEdit()) return;
                _session.UpdateTextEditDraft("");
                _session.CommitTextEdit();
            };
            input.PointerPressed += async (_, args) =>
            {
                if (!args.GetCurrentPoint(input).Properties.IsLeftButtonPressed ||
                    (args.Source is Control source &&
                     (ReferenceEquals(clear, source) || clear.IsVisualAncestorOf(source)))) return;
                args.Handled = true;
                await ShowWatermarkEditorAsync(input);
            };
            AutomationProperties.SetName(input, UiText.Get("AnnotationWatermarkInput"));
            _refresh.Add(() =>
            {
                var text = _session.Properties.WatermarkText;
                var lineEnd = text.IndexOf('\n');
                input.Text = lineEnd < 0 ? text : text[..lineEnd] + "…";
                clear.IsVisible = text.Length != 0;
            });
            row.Children.Add(input);
            var positions = new ComboBox
            {
                ItemsSource = new[]
                {
                    UiText.Get("AnnotationTiled"), UiText.Get("AnnotationWatermarkBottomRight"),
                    UiText.Get("AnnotationWatermarkBottomLeft"), UiText.Get("AnnotationWatermarkTopRight"),
                    UiText.Get("AnnotationWatermarkTopLeft"), UiText.Get("AnnotationWatermarkTopCenter"),
                    UiText.Get("AnnotationWatermarkBottomCenter"), UiText.Get("AnnotationWatermarkCenter"),
                },
                Width = 100, Height = 28, MinHeight = 0, Padding = new(4, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            int[] anchors = [4, 8, 6, 2, 0, 1, 7, 4];
            ToolTip.SetTip(positions, UiText.Get("AnnotationWatermarkAnchor"));
            positions.SelectionChanged += async (_, _) =>
            {
                if (_disposed || _refreshing || positions.SelectedIndex < 0) return;
                var index = positions.SelectedIndex;
                await _session.UpdateSelectedPropertiesAsync(_session.Properties with
                {
                    Tiled = index == 0, NineGrid = index != 0, Anchor = anchors[index],
                });
            };
            _refresh.Add(() => positions.SelectedIndex = _session.Properties.Tiled
                ? 0
                : Math.Max(1, Array.IndexOf(anchors, _session.Properties.Anchor, 1)));
            row.Children.Add(positions);
            var style = Button("AnnotationWatermarkStyle", icon: "AnimationText");
            AttachHoverPanel(style, WatermarkStylePanel);
            row.Children.Add(style);
            row.Children.Add(Font());
            return row;
        }

        private Control WatermarkStylePanel()
        {
            var panel = new StackPanel { Spacing = 3 };
            panel.Children.Add(PropertySlider("AnnotationOpacity", 10, 100,
                () => _session.Properties.Opacity * 100,
                value => _session.UpdateSelectedPropertiesAsync(_session.Properties with { Opacity = value / 100 }),
                "%"));
            panel.Children.Add(PropertySlider("AnimationFontSize", 8, 80,
                () => _session.FontSize, value => _session.UpdateSelectedStyleAsync(fontSize: value)));
            panel.Children.Add(PropertySlider("AnnotationWatermarkSpacing", 0, 100,
                () => _session.Properties.WatermarkSpacing,
                value => _session.UpdateSelectedPropertiesAsync(_session.Properties with
                {
                    WatermarkSpacing = value,
                })));
            panel.Children.Add(Palette());
            return panel;
        }
    }
}

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        public Control WatermarkSource()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var image = Button("AnnotationWatermarkImage", icon: "AnimationAddImage");
            image.Click += async (_, _) =>
            {
                if (!_disposed) await _loadWatermark();
            };
            panel.Children.Add(image);
            var text = Button("AnnotationWatermarkText", icon: "AnimationText");
            text.Click += (_, _) => ShowTextEditor(text);
            panel.Children.Add(text);
            return panel;
        }

        public Control WatermarkLayout()
        {
            return Popup("AnnotationNineGrid", "SettingsAnnotationHeading", () =>
            {
                var panel = new StackPanel { Spacing = 6 };
                panel.Children.Add(Labeled("AnnotationTiled", Toggle("AnnotationTiled", "",
                    p => p.Tiled, (p, value) => p with { Tiled = value }, "AnnotationToolWatermark")));
                panel.Children.Add(Labeled("AnnotationNineGrid", Toggle("AnnotationNineGrid", "",
                    p => p.NineGrid, (p, value) => p with { NineGrid = value }, "AnimationRectangle")));
                var anchors = new UniformGrid { Rows = 3, Columns = 3, Width = 96 };
                string[] labels = ["↖", "↑", "↗", "←", "●", "→", "↙", "↓", "↘"];
                for (var index = 0; index < 9; index++)
                {
                    var anchor = index;
                    var button = Button("AnnotationNineGrid", labels[index]);
                    button.Click += async (_, _) =>
                        await _session.UpdateSelectedPropertiesAsync(_session.Properties with { Anchor = anchor });
                    _refresh.Add(() => button.Classes.Set("active-tool", _session.Properties.Anchor == anchor));
                    anchors.Children.Add(button);
                }

                panel.Children.Add(anchors);
                panel.Children.Add(Labeled("AnnotationWatermarkWidth", PropertyNumber("AnnotationWatermarkWidth",
                    1, 8192, p => p.ImageWidth, (p, value) => p with { ImageWidth = value }, true)));
                panel.Children.Add(Labeled("AnnotationTextPadding", PropertyNumber("AnnotationTextPadding", 0, 128,
                    p => p.TextPadding, (p, value) => p with { TextPadding = value })));
                panel.Children.Add(Labeled("AnnotationWrapWidth", PropertyNumber("AnnotationWrapWidth", 0, 8192,
                    p => p.WrapWidth, (p, value) => p with { WrapWidth = value }, true)));
                panel.Children.Add(TextStyles());
                panel.Children.Add(TextOutline());
                panel.Children.Add(TextBackground());
                return panel;
            });
        }
    }
}

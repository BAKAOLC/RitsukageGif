using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        private static readonly (string Label, string Format)[] WatermarkTimeFormats =
        [
            ("ISO 24h", "$yyyy-MM-dd HH:mm:ss$"), ("Slash 24h", "$yyyy/MM/dd HH:mm:ss$"),
            ("EU 24h", "$dd/MM/yyyy HH:mm:ss$"), ("US 24h", "$MM/dd/yyyy HH:mm:ss$"),
            ("Date", "$yyyy-MM-dd$"), ("Time", "$HH:mm:ss$"), ("Time 12h", "$hh:mm:ss a$"),
            ("Weekday", "$ddd yyyy-MM-dd HH:mm:ss$"), ("Millis", "$yyyy-MM-dd HH:mm:ss.zzz$"),
            ("Compact", "$yyyyMMddHHmmss$"), ("US 12h", "$yyyy-MM-dd hh:mm:ss a$"),
        ];

        private Control WatermarkTextEditorContent(TextBox text, Control? actions = null)
        {
            if (!_session.IsEditingWatermark) return text;
            var selected =
                WatermarkTimeFormats.FirstOrDefault(item => item.Format == _session.Properties.WatermarkTimeFormat);
            if (selected == default) selected = WatermarkTimeFormats[0];
            var insert = new SplitButton { Content = selected.Label, MinHeight = 26, Padding = new(6, 2) };
            var menu = new Flyout { Placement = PlacementMode.Bottom };
            var choices = new StackPanel { Spacing = 0 };
            foreach (var option in WatermarkTimeFormats)
            {
                var item = new Button
                {
                    Content = option.Label, HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left, MinHeight = 26,
                };
                ToolTip.SetTip(item, option.Format);
                item.Click += (_, _) =>
                {
                    selected = option;
                    _session.SetWatermarkTimeFormat(option.Format);
                    insert.Content = option.Label;
                    ToolTip.SetTip(insert, option.Format);
                    menu.Hide();
                    Insert();
                };
                choices.Children.Add(item);
            }

            menu.Content = choices;
            insert.Flyout = menu;
            insert.Click += (_, _) => Insert();
            ToolTip.SetTip(insert, selected.Format);
            var time = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children =
                {
                    new TextBlock
                        { Text = UiText.Get("AnnotationWatermarkTime"), VerticalAlignment = VerticalAlignment.Center },
                    insert,
                },
            };
            var footer = new Grid { ColumnDefinitions = new("*,Auto") };
            footer.Children.Add(time);
            if (actions is not null)
            {
                Grid.SetColumn(actions, 1);
                footer.Children.Add(actions);
            }

            var layout = new Grid { RowDefinitions = new("*,Auto"), RowSpacing = 4 };
            layout.Children.Add(text);
            Grid.SetRow(footer, 1);
            layout.Children.Add(footer);
            return layout;

            void Insert()
            {
                text.SelectedText = selected.Format;
                text.Focus();
            }
        }
    }
}

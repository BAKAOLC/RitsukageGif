using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnnotationPropertyView
    {
        private Control WatermarkTextEditorContent(TextBox text)
        {
            if (!_session.IsEditingWatermark) return text;
            var insert = new Button
            {
                Content = UiText.Get("AnnotationWatermarkInsertTime"), MinHeight = 26,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            var menu = new ContextMenu();
            foreach (var (key, value) in new[]
                     {
                         ("AnnotationWatermarkDate", "$yyyy-MM-dd$"),
                         ("AnnotationWatermarkTime", "$HH:mm:ss$"),
                         ("AnnotationWatermarkDateTime", "$yyyy-MM-dd HH:mm:ss$"),
                     })
            {
                var item = new MenuItem { Header = UiText.Get(key) };
                item.Click += (_, _) =>
                {
                    text.SelectedText = value;
                    text.Focus();
                };
                menu.Items.Add(item);
            }

            insert.ContextMenu = menu;
            insert.Click += (_, _) => menu.Open(insert);
            ToolTip.SetTip(insert, UiText.Get("AnnotationWatermarkTimeHint"));
            return new StackPanel { Spacing = 4, Children = { text, insert } };
        }
    }
}

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SettingsWindow
    {
        private Button ContentsSelector(bool[] values, string[] keys, Action? changed = null)
        {
            var button = new Button { HorizontalContentAlignment = HorizontalAlignment.Stretch };
            var options = new StackPanel { Spacing = 4, Margin = new(8) };
            var summary = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
            var content = new Grid { ColumnDefinitions = new("*,16"), ColumnSpacing = 8 };
            content.Children.Add(summary);
            var arrow = DropDownChevron.Create(button);
            Grid.SetColumn(arrow, 1);
            content.Children.Add(arrow);
            button.Content = content;

            for (var index = 0; index < keys.Length; index++)
            {
                var item = index;
                var check = Toggle(values[item]);
                check.Bind(ContentProperty, LiveText.Resource(keys[item]));
                check.IsCheckedChanged += (_, _) =>
                {
                    values[item] = check.IsChecked == true;
                    Refresh();
                    changed?.Invoke();
                    RequestLiveApply();
                };
                options.Children.Add(check);
            }

            button.Flyout = new Flyout { Content = options };
            Refresh();
            SubscribeContentLanguage(Refresh);
            return button;

            void Refresh()
            {
                summary.Text = string.Join("; ", keys.Where((_, index) => values[index]).Select(UiText.Get));
                if (summary.Text.Length == 0) summary.Text = UiText.Get("CaptureContentsNone");
                ToolTip.SetTip(button, summary.Text);
            }
        }
    }
}

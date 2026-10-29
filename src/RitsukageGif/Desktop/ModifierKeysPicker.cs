using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class ModifierKeysPicker : Button
    {
        internal ModifierKeysPicker(KeyModifiers value)
        {
            Value = value;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            var choices = new StackPanel { Spacing = 4, MinWidth = 120 };
            foreach (var (modifier, label) in new[]
                     {
                         (KeyModifiers.Shift, "Shift"), (KeyModifiers.Control, "Ctrl"),
                         (KeyModifiers.Alt, "Alt"), (KeyModifiers.Meta, OperatingSystem.IsMacOS() ? "Cmd" : "Win"),
                     })
            {
                var choice = new CheckBox { Content = label, IsChecked = value.HasFlag(modifier) };
                choice.IsCheckedChanged += (_, _) =>
                {
                    Value = choice.IsChecked == true ? Value | modifier : Value & ~modifier;
                    RefreshLabel();
                    ValueChanged?.Invoke();
                };
                choices.Children.Add(choice);
            }

            Flyout = new Flyout { Content = choices };
            RefreshLabel();
        }

        protected override Type StyleKeyOverride => typeof(Button);

        internal KeyModifiers Value { get; private set; }

        internal event Action? ValueChanged;

        private void RefreshLabel()
        {
            var names = new List<string>(4);
            if (Value.HasFlag(KeyModifiers.Shift)) names.Add("Shift");
            if (Value.HasFlag(KeyModifiers.Control)) names.Add("Ctrl");
            if (Value.HasFlag(KeyModifiers.Alt)) names.Add("Alt");
            if (Value.HasFlag(KeyModifiers.Meta)) names.Add(OperatingSystem.IsMacOS() ? "Cmd" : "Win");
            var text = names.Count == 0 ? UiText.Get("SettingsNoModifier") : string.Join(" + ", names);
            var content = new Grid { ColumnDefinitions = new("*,16"), ColumnSpacing = 8 };
            content.Children.Add(new TextBlock
            {
                Text = text, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var arrow = DropDownChevron.Create(this);
            Grid.SetColumn(arrow, 1);
            content.Children.Add(arrow);
            Content = content;
            ToolTip.SetTip(this, text);
            AutomationProperties.SetName(this, UiText.Get("GlobalMouseModifiers") + ": " + text);
        }
    }
}

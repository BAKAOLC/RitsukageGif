using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedGroupEditorFields : StackPanel
    {
        private readonly AppearanceColorPicker _color = new()
        {
            Name = "PinnedGroupColor", Width = 100, Height = 28, MinHeight = 0,
            Color = Colors.Transparent, IsEnabled = false,
            IsAlphaEnabled = true, IsAlphaVisible = true, IsColorPaletteVisible = true,
            PaletteColors =
            [
                Color.Parse("#fa030f"), Color.Parse("#f87610"), Color.Parse("#f4cf51"),
                Color.Parse("#72cc57"), Color.Parse("#3388ff"), Color.Parse("#d289e2"),
            ],
            PaletteColumnCount = 6,
            IsComponentTextInputVisible = true, IsHexInputVisible = true,
            HexInputAlphaPosition = AlphaComponentPosition.Leading,
        };

        private readonly CheckBox _setColor = new()
        {
            [!ContentControl.ContentProperty] = LiveText.Resource("PinnedGroupSetColor"),
        };

        private bool _hasColor;

        internal PinnedGroupEditorFields(bool creating)
        {
            Spacing = 6;
            Children.Add(new TextBlock
            {
                [!TextBlock.TextProperty] = LiveText.Resource(creating ? "PinnedGroupNewName" : "PinnedGroupName"),
            });
            Children.Add(NameInput);
            _color.Styles.Add(new AnnotationColorPickerStyles());
            var swatch = new Border { Background = new SolidColorBrush(_color.Color), Margin = new(1) };
            _color.Content = swatch;
            _color.ColorChanged += (_, _) =>
            {
                _hasColor = true;
                swatch.Background = new SolidColorBrush(_color.Color);
            };
            _setColor.IsCheckedChanged += (_, _) => _color.IsEnabled = _setColor.IsChecked == true;
            Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { _setColor, _color },
            });
        }

        internal TextBox NameInput { get; } = new()
        {
            Name = "PinnedGroupName", MaxLength = 32767,
            [!TextBox.PlaceholderTextProperty] = LiveText.Resource("PinnedGroupEnterName"),
        };

        internal string GroupName => NameInput.Text ?? "";
        internal string? GroupColor => _setColor.IsChecked == true && _hasColor ? _color.Color.ToString() : null;

        internal void Load(string name, string? color)
        {
            NameInput.Text = name;
            _setColor.IsChecked = color is not null;
            _color.Color = color is not null ? Color.Parse(color) : Colors.Transparent;
            _hasColor = color is not null;
        }
    }
}

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationEditorWindow
    {
        private const string EditorFontResource = "AnimationEditorFontSize";
        private const string HeadingFontResource = "AnimationEditorHeadingSize";
        private const string CaptionFontResource = "AnimationEditorCaptionSize";
        private const string ButtonHeightResource = "AnimationEditorButtonHeight";
        private const string IconSizeResource = "AnimationEditorIconSize";

        private void ConfigureTypography()
        {
            FontSize = 13;
            UpdateTypography();
            foreach (var type in new[]
                     {
                         typeof(Button), typeof(TextBox), typeof(ComboBox), typeof(CheckBox), typeof(NumericUpDown),
                         typeof(TabItem)
                     })
                Styles.Add(new Style(s => s.OfType(type))
                {
                    Setters =
                    {
                        new Setter(TemplatedControl.FontSizeProperty, new DynamicResourceExtension(EditorFontResource)),
                        new Setter(MinHeightProperty, new DynamicResourceExtension(ButtonHeightResource))
                    }
                });
            Styles.Add(new Style(s => s.OfType<TextBlock>().Class("editor-caption"))
            {
                Setters = { new Setter(TextBlock.FontSizeProperty, new DynamicResourceExtension(CaptionFontResource)) }
            });
            PropertyChanged += (_, change) =>
            {
                if (change.Property == FontSizeProperty) UpdateTypography();
            };
            _propertyTitle[!TextBlock.FontSizeProperty] = new DynamicResourceExtension(HeadingFontResource);
            _frameSummary.Classes.Add("editor-caption");
            _status.Classes.Add("editor-caption");
        }

        private void UpdateTypography()
        {
            var size = FontSize;
            Resources[EditorFontResource] = size;
            Resources[HeadingFontResource] = size * 14 / 13;
            Resources[CaptionFontResource] = size * 12 / 13;
            Resources[ButtonHeightResource] = size * 28 / 13;
            Resources[IconSizeResource] = size * 20 / 13;
        }

        private static TextBlock Heading(string key)
        {
            var heading = new TextBlock { [!TextBlock.TextProperty] = LiveText.Resource(key) };
            heading[!TextBlock.FontSizeProperty] = new DynamicResourceExtension(HeadingFontResource);
            return heading;
        }
    }
}

using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace RitsukageGif.Desktop
{
    internal sealed class TextHighlightColorPickerStyles : Styles
    {
        private static readonly FuncControlTemplate<DropDownButton> CircleTemplate = new((owner, _) => new Border
        {
            Name = "TextHighlightBorder", CornerRadius = new(14), BorderThickness = new(2),
            BorderBrush = Brushes.Gray, Background = Brushes.Transparent,
            Child = new ContentPresenter
            {
                [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty],
            },
        });

        internal TextHighlightColorPickerStyles()
        {
            Add(new AnnotationColorPickerStyles());
            Add(new Style(selector => selector.OfType<ColorPicker>().Template().OfType<DropDownButton>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.TemplateProperty, CircleTemplate),
                    new Setter(Layoutable.MinWidthProperty, 0d),
                    new Setter(Layoutable.MinHeightProperty, 0d),
                },
            });
            Add(new Style(selector => selector.OfType<ColorPicker>().Template().OfType<DropDownButton>()
                .Class(":pointerover").Template().OfType<Border>().Name("TextHighlightBorder"))
            {
                Setters = { new Setter(Border.BorderBrushProperty, AppTheme.Ink) },
            });
        }
    }
}

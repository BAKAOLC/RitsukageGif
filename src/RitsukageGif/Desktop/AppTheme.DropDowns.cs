using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Styling;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AppTheme
    {
        private void AddDropDowns()
        {
            foreach (var type in new[] { typeof(ComboBox), typeof(DropDownButton), typeof(SplitButton) })
                Add(new Style(s => type == typeof(SplitButton)
                    ? s.OfType(type).Template().OfType<PathIcon>()
                    : s.OfType(type).Template().OfType<PathIcon>().Name("DropDownGlyph"))
                {
                    Setters =
                    {
                        new Setter(TemplatedControl.TemplateProperty, DropDownChevron.Template),
                        new Setter(Layoutable.WidthProperty, 16d),
                        new Setter(Layoutable.HeightProperty, 16d),
                        new Setter(Layoutable.MinWidthProperty, 0d),
                        new Setter(Layoutable.MinHeightProperty, 0d),
                        new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Center),
                        new Setter(Layoutable.MarginProperty, new Thickness(0)),
                        new Setter(Visual.OpacityProperty, 1d),
                    },
                });
        }
    }
}

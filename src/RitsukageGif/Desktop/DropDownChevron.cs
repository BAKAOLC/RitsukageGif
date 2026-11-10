using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal static class DropDownChevron
    {
        public static Geometry Data { get; } = IconCatalog.GetGeometry("ToolbarChevron");

        public static IControlTemplate Template { get; } = new FuncControlTemplate<PathIcon>((_, _) => new SvgIcon(
            ToolbarIcon.GetDefinition("ToolbarChevron"))
        {
            Width = 16, Height = 16,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            [!TemplatedControl.ForegroundProperty] = new DynamicResourceExtension("DropDownChevronBrush"),
        });

        internal static PathIcon Create(Button owner)
        {
            return new()
            {
                Data = Data, Template = Template, Width = 16, Height = 16,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                [!TemplatedControl.ForegroundProperty] = owner[!TemplatedControl.ForegroundProperty],
            };
        }
    }
}

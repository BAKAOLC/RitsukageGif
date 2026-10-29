using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace RitsukageGif.Desktop
{
    internal static class DropDownChevron
    {
        public static Geometry Data { get; } = Geometry.Parse(FluentToolbarIcon.Geometry("ToolbarChevron")!);


        public static IControlTemplate Template { get; } = new FuncControlTemplate<PathIcon>((_, _) => new Viewbox
        {
            Width = 16, Height = 16,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = new Canvas
            {
                Width = 24, Height = 24,
                Children =
                {
                    new Path
                    {
                        Data = Data, Width = 24, Height = 24,
                        [!Shape.FillProperty] = new DynamicResourceExtension("DropDownChevronBrush"),
                    },
                },
            },
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

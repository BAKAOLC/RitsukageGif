using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal static class PinnedGroupIcon
    {
        internal static Control Create(TemplatedControl owner, PinnedGroupDefinition definition, bool active)
        {
            if (active && definition.Color is null)
                return ToolbarIcon.CreateInViewport(owner, ToolbarIcon.Geometry("ScreenshotApplyText"));
            IBrush brush = definition.Color is { } color
                ? new SolidColorBrush(Color.Parse(color))
                : Brushes.Transparent;
            var dot = new Border
            {
                Width = 10, Height = 10, CornerRadius = new(5), Background = brush,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            if (!active) return dot;
            dot.Width = 6;
            dot.Height = 6;
            return new Border
            {
                Width = 16, Height = 16, CornerRadius = new(8), BorderThickness = new(1.5), BorderBrush = brush,
                Child = dot, VerticalAlignment = VerticalAlignment.Center,
            };
        }
    }
}

using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedTextSelectionSurface(Action<DrawingContext> render) : Control
    {
        public override void Render(DrawingContext context)
        {
            render(context);
        }
    }
}

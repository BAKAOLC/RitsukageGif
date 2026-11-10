using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class SelectionHintsSurface(OverlayCanvas canvas) : Control
    {
        public override void Render(DrawingContext context)
        {
            canvas.RenderHints(context);
        }
    }
}

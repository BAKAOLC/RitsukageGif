using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering;

namespace RitsukageGif.Desktop
{
    internal sealed class ImageEditorStripSurface(Action<DrawingContext> render) : Control, ICustomHitTest
    {
        public bool HitTest(Point point)
        {
            return IsHitTestVisible && new Rect(Bounds.Size).Contains(point);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            render(context);
        }
    }
}

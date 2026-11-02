using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class TableRecognitionHeaders(TableRecognitionGrid owner, int kind) : Control
    {
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            owner.DrawHeader(context, kind, Bounds.Size);
        }
    }
}

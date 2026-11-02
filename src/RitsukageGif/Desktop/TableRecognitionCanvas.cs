using Avalonia.Controls;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class TableRecognitionCanvas(TableRecognitionGrid owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            owner.DrawTable(context);
        }
    }
}

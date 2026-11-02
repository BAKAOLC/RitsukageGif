using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedRichImageRun(
        PinnedRichImage image,
        Size size,
        TextRunProperties properties,
        Action<DrawingContext, PinnedRichImage, Rect> draw) : DrawableTextRun
    {
        public override Size Size => size;
        public override double Baseline => size.Height;
        public override ReadOnlyMemory<char> Text => "\ufffc".AsMemory();
        public override TextRunProperties Properties => properties;

        public override void Draw(DrawingContext drawingContext, Point origin)
        {
            draw(drawingContext, image, new(origin, size));
        }
    }
}

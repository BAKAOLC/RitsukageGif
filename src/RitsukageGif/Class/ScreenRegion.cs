using System.Drawing;

namespace RitsukageGif.Class
{
    public class ScreenRegion(ScreenInfo screen, RectangleF rectangle)
    {
        public ScreenInfo Screen { get; } = screen;

        public RectangleF Rectangle { get; } = rectangle;

        public override string ToString()
        {
            return $"Screen: {Screen}, Rectangle: {Rectangle}";
        }
    }
}
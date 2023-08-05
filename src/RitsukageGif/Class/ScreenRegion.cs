using System.Drawing;

namespace RitsukageGif.Class
{
    public class ScreenRegion
    {
        public ScreenRegion(ScreenInfo screen, RectangleF rectangle)
        {
            Screen = screen;
            Rectangle = rectangle;
        }

        public ScreenInfo Screen { get; }

        public RectangleF Rectangle { get; }

        public override string ToString()
        {
            return $"Screen: {Screen}, Rectangle: {Rectangle}";
        }
    }
}
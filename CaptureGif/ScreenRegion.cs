using System.Drawing;

namespace CaptureGif
{
    public class ScreenRegion
    {
        public ScreenInfo Screen { get; }

        public RectangleF Rectangle { get; }

        public ScreenRegion(ScreenInfo screen, RectangleF rectangle)
        {
            Screen = screen;
            Rectangle = rectangle;
        }

        public override string ToString()
        {
            return $"Screen: {Screen}, Rectangle: {Rectangle}";
        }
    }
}

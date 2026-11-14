using Avalonia;

namespace RitsukageGif.Desktop
{
    internal static class SelectionMagnifierPlacement
    {
        internal static Rect Place(Size viewport, Size size, Point pointer, double scale)
        {
            var horizontalGap = Math.Round(10 * scale, MidpointRounding.AwayFromZero) / scale;
            var verticalGap = Math.Round(20 * scale, MidpointRounding.AwayFromZero) / scale;
            var pixel = 1 / scale;
            var x = pointer.X + horizontalGap;
            var y = pointer.Y + verticalGap;
            if (x + size.Width >= viewport.Width - pixel) x = pointer.X - horizontalGap - size.Width;
            if (y + size.Height >= viewport.Height - pixel) y = pointer.Y - verticalGap - size.Height;
            return new(Math.Clamp(x, 0, Math.Max(0, viewport.Width - size.Width)),
                Math.Clamp(y, 0, Math.Max(0, viewport.Height - size.Height)), size.Width, size.Height);
        }
    }
}

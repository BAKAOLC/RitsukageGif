using Avalonia;

namespace RitsukageGif.Desktop
{
    internal static class SelectionMagnifierPlacement
    {
        internal static Rect Place(Size viewport, Size size, Point pointer)
        {
            const double horizontalGap = 10;
            const double verticalGap = 20;
            var x = pointer.X + horizontalGap;
            var y = pointer.Y + verticalGap;
            if (x + size.Width >= viewport.Width) x = pointer.X - horizontalGap - size.Width;
            if (y + size.Height >= viewport.Height) y = pointer.Y - verticalGap - size.Height;
            return new(Math.Clamp(x, 0, Math.Max(0, viewport.Width - size.Width)),
                Math.Clamp(y, 0, Math.Max(0, viewport.Height - size.Height)), size.Width, size.Height);
        }
    }
}

using Avalonia;

namespace RitsukageGif.Desktop
{
    internal static class ScreenshotToolbarLayout
    {
        internal static Point Place(Size viewport, Rect selection, Size toolbar)
        {
            const double margin = 8;
            const double gap = 10;
            var maximumX = Math.Max(margin, viewport.Width - toolbar.Width - margin);
            var maximumY = Math.Max(margin, viewport.Height - toolbar.Height - margin);
            var x = Math.Clamp(selection.Right - toolbar.Width, margin, maximumX);
            var below = selection.Bottom + gap;
            var above = selection.Top - toolbar.Height - gap;
            var y = below + toolbar.Height <= viewport.Height - margin
                ? below
                : above >= margin
                    ? above
                    : selection.Bottom - toolbar.Height - gap;
            return new(x, Math.Clamp(y, margin, maximumY));
        }
    }
}

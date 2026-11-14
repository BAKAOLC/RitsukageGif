using Avalonia;

namespace RitsukageGif.Desktop
{
    internal static class ScreenshotToolbarLayout
    {
        internal static (Point Position, bool Above) PlaceFloatingCapture(Size viewport, Rect selection, Size toolbar)
        {
            const double margin = 8;
            const double gap = 10;
            var x = Math.Clamp(selection.Right - toolbar.Width, margin,
                Math.Max(margin, viewport.Width - toolbar.Width - margin));
            var below = selection.Bottom + gap;
            if (below + toolbar.Height <= viewport.Height - margin) return (new(x, below), false);

            var above = selection.Top - toolbar.Height - gap;
            var y = above >= margin ? above : Math.Max(margin, selection.Bottom - toolbar.Height);
            return (new(x, y), true);
        }

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

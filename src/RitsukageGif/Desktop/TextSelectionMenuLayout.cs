using Avalonia;

namespace RitsukageGif.Desktop
{
    internal readonly record struct TextSelectionMenuLayout(
        Rect PopupBounds,
        Rect BodyBounds,
        Rect PaletteBounds,
        bool Above,
        double ArrowX)
    {
        internal static TextSelectionMenuLayout Calculate(Rect target, Rect workArea, Size body, Size palette,
            bool paletteVisible)
        {
            const double arrow = 9;
            const double gap = 8;
            var required = body.Height + arrow + palette.Height + gap;
            var topRoom = target.Top - workArea.Top;
            var bottomRoom = workArea.Bottom - target.Bottom;
            var above = topRoom >= required || bottomRoom < required;
            var x = Math.Max(workArea.Left, Math.Min(target.Center.X - body.Width / 2, workArea.Right - body.Width));
            var bodyY = above
                ? (topRoom < required && bottomRoom < required ? target.Center.Y : target.Top) - body.Height - arrow
                : target.Bottom + arrow;
            var extra = paletteVisible ? palette.Height + gap : 0;
            var popupY = above ? bodyY - extra : bodyY - arrow;
            var bodyTop = above ? extra : arrow;
            return new(
                new(x, popupY, body.Width, body.Height + arrow + extra),
                new(0, bodyTop, body.Width, body.Height),
                new(0, above ? 0 : bodyTop + body.Height + gap, palette.Width,
                    paletteVisible ? palette.Height : 0),
                above, Math.Clamp(target.Center.X - x, 25, body.Width - 25));
        }
    }
}

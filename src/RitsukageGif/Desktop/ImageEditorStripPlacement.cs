using Avalonia;

namespace RitsukageGif.Desktop
{
    internal static class ImageEditorStripPlacement
    {
        internal static Rect Place(Rect band, Rect viewport, Size button, ImageStripAxis axis)
        {
            var available = viewport.Deflate(8);
            var centered = new Rect(band.Center.X - button.Width / 2, band.Center.Y - button.Height / 2,
                button.Width, button.Height);
            if (band.Contains(centered) && available.Contains(centered)) return centered;
            var x = Math.Clamp(centered.X, available.X, Math.Max(available.X, available.Right - button.Width));
            var y = Math.Clamp(centered.Y, available.Y, Math.Max(available.Y, available.Bottom - button.Height));
            if (axis == ImageStripAxis.Rows)
            {
                var below = new Rect(x, band.Bottom + 8, button.Width, button.Height);
                if (available.Contains(below)) return below;
                var above = new Rect(x, band.Y - button.Height - 8, button.Width, button.Height);
                if (available.Contains(above)) return above;
            }
            else
            {
                var left = new Rect(band.X - button.Width - 8, y, button.Width, button.Height);
                if (available.Contains(left)) return left;
                var right = new Rect(band.Right + 8, y, button.Width, button.Height);
                if (available.Contains(right)) return right;
            }

            return new(x, y, button.Width, button.Height);
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace RitsukageGif.Desktop
{
    internal sealed class ImageTranslationCanvas(
        IImage? image,
        IReadOnlyList<(TextLayout Layout, Rect Bounds)> layouts) : Control
    {
        public override void Render(DrawingContext context)
        {
            if (image is not null) context.DrawImage(image, new(0, 0, image.Size.Width, image.Size.Height));
            foreach (var (layout, bounds) in layouts)
            {
                using var clip = context.PushClip(bounds);
                layout.Draw(context, new(bounds.X, bounds.Y + (bounds.Height - layout.Height) / 2));
            }
        }
    }
}

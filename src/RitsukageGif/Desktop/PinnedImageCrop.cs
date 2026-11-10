using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedImageCrop(IImage source, PixelRect region) : IImage
    {
        public IImage Source { get; set; } = source;
        public PixelRect Region { get; set; } = region;
        public Size Size => new(Region.Width, Region.Height);

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
            Source.Draw(context, sourceRect.Translate(new(Region.X, Region.Y)), destRect);
        }
    }
}

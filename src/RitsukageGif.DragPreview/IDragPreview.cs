using Avalonia;
using Avalonia.Media.Imaging;

namespace RitsukageGif.DragPreview
{
    public interface IDragPreview
    {
        Bitmap Preview { get; }
        PixelPoint HotSpot { get; }
        double PixelScale { get; }
    }
}

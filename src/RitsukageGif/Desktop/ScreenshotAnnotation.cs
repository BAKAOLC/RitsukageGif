using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed class ScreenshotAnnotation : IDisposable
    {
        public required ScreenshotTool Tool { get; init; }
        public required Color Color { get; init; }
        public required double Thickness { get; init; }
        public List<Point> Points { get; } = [];
        public string Text { get; init; } = "";
        public double FontSize { get; init; } = 24;
        public int MosaicSize { get; init; } = 12;
        public ScreenshotAnnotationProperties Properties { get; init; } = new();
        public ScreenshotAnnotationImage? WatermarkImage { get; init; }
        public IReadOnlyList<(Bitmap Image, Rect Bounds)> ImageTiles { get; set; } = [];

        public Rect Bounds => Points.Count < 2
            ? new(Points.FirstOrDefault(), new Size(1, 1))
            : new Rect(Points[0], Points[^1]).Normalize();

        public void Dispose()
        {
            foreach (var tile in ImageTiles) tile.Image.Dispose();
            WatermarkImage?.Dispose();
        }
    }
}

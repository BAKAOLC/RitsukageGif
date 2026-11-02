using Avalonia;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed class MagnifierSourceState(ScreenshotAnnotation annotation)
    {
        private readonly Vector _magnifierOffset = annotation.MagnifierOffset;
        private readonly Point[] _points = [.. annotation.Points];
        private readonly ScreenshotAnnotationProperties _properties = annotation.Properties;
        private readonly IReadOnlyList<(Bitmap Image, Rect Bounds)> _tiles = annotation.ImageTiles;

        public bool Matches(ScreenshotAnnotation candidate)
        {
            return ReferenceEquals(annotation, candidate) && _properties == candidate.Properties &&
                   _magnifierOffset == candidate.MagnifierOffset &&
                   ReferenceEquals(_tiles, candidate.ImageTiles) && _points.SequenceEqual(candidate.Points);
        }
    }
}

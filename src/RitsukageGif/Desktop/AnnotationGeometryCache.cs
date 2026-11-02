using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class AnnotationGeometryCache
    {
        public required Point[] Points { get; init; }
        public required ScreenshotAnnotationProperties Properties { get; init; }
        public required double Thickness { get; init; }
        public required Geometry Path { get; init; }
        public bool PathFilled { get; init; }
        public double OutlineWidth { get; init; }
        public bool SmoothStroke { get; init; }
        public IPen? ArrowOutlinePen { get; set; }
        public Geometry? Shaft { get; init; }
        public Geometry? StartHead { get; init; }
        public Geometry? EndHead { get; init; }
        public bool StartFilled { get; init; }
        public bool EndFilled { get; init; }
        public Rect ShadeBounds { get; set; }
        public Geometry? Shade { get; set; }
    }
}

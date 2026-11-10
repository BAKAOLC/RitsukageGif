using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class AnnotationNumberLayout
    {
        public required ScreenshotAnnotationProperties Properties { get; init; }
        public required Color Color { get; init; }
        public required double FontSize { get; init; }
        public required double Thickness { get; init; }
        public uint? ForegroundColor { get; init; }
        public required AnnotationSizeLevel TextSize { get; init; }
        public required AnnotationSizeLevel ArrowSize { get; init; }
        public required string Caption { get; init; }
        public required Point Center { get; init; }
        public required Point Target { get; init; }
        public Point? CaptionAnchor { get; init; }
        public required Rect MarkerBounds { get; init; }
        public required Rect Bounds { get; init; }
        public required FormattedText Label { get; init; }
        public required Point LabelOrigin { get; init; }
        public required IBrush Ink { get; init; }
        public required IPen Outline { get; init; }
        public required IPen LeaderPen { get; init; }
        public Geometry? Marker { get; init; }
        public Geometry? LeaderHead { get; init; }
        public bool FilledHead { get; init; }
        public Point LeaderStart { get; init; }
        public Point LeaderEnd { get; init; }
        public Geometry? Callout { get; init; }
        public FormattedText? CaptionText { get; init; }
        public Geometry? CaptionOutline { get; init; }
        public IPen? CaptionOutlinePen { get; init; }
        public Point CaptionOrigin { get; init; }
    }
}

using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotation
    {
        private ScreenshotAnnotationRecord? _documentRecord;

        internal ScreenshotAnnotationRecord CaptureDocumentRecord()
        {
            if (_documentRecord is { } cached && cached.Properties == Properties &&
                cached.MagnifierOffset == new AnnotationDocumentPoint(MagnifierOffset.X, MagnifierOffset.Y) &&
                MatchesDocumentPoints(cached.Points)) return cached;
            return _documentRecord = new()
            {
                Tool = Tool, Color = Color, Thickness = Thickness,
                Points = [.. Points.Select(point => new AnnotationDocumentPoint(point.X, point.Y))],
                Text = Text, FontSize = FontSize, MosaicSize = MosaicSize, Properties = Properties with { },
                MagnifierOffset = new(MagnifierOffset.X, MagnifierOffset.Y), AutoMosaicId = AutoMosaicId,
                AutoMosaicRegions = AutoMosaicRegions.IsDefault
                    ? []
                    : [.. AutoMosaicRegions.Select(ToDocumentRegion)],
                AutoMosaicCandidates = AutoMosaicCandidates.IsDefault ? [] : [.. AutoMosaicCandidates],
                AutoMosaicCandidateBounds = ToDocumentRegion(AutoMosaicCandidateBounds),
                ImageId = WatermarkImage?.Id, WatermarkTimestamp = WatermarkTimestamp,
            };
        }

        private bool MatchesDocumentPoints(AnnotationDocumentPoint[] points)
        {
            if (points.Length != Points.Count) return false;
            for (var index = 0; index < points.Length; index++)
                if (!points[index].X.Equals(Points[index].X) || !points[index].Y.Equals(Points[index].Y))
                    return false;
            return true;
        }

        private static AnnotationDocumentRegion ToDocumentRegion(Rect bounds)
        {
            return new(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }
    }
}

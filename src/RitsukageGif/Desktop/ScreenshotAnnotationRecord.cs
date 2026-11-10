using System.Text.Json.Serialization;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed record ScreenshotAnnotationRecord
    {
        public required ScreenshotTool Tool { get; init; }

        [JsonConverter(typeof(AnnotationColorJsonConverter))]
        public required Color Color { get; init; }

        public required double Thickness { get; init; }
        public required AnnotationDocumentPoint[] Points { get; init; }
        public string Text { get; init; } = "";
        public double FontSize { get; init; } = 24;
        public int MosaicSize { get; init; } = 5;
        public ScreenshotAnnotationProperties Properties { get; init; } = new();
        public AnnotationDocumentPoint MagnifierOffset { get; init; }
        public Guid AutoMosaicId { get; init; }
        public AnnotationDocumentRegion[] AutoMosaicRegions { get; init; } = [];
        public PrivacyCandidate?[] AutoMosaicCandidates { get; init; } = [];
        public AnnotationDocumentRegion AutoMosaicCandidateBounds { get; init; }
        public string? ImageId { get; init; }
        public DateTimeOffset WatermarkTimestamp { get; init; }
    }
}

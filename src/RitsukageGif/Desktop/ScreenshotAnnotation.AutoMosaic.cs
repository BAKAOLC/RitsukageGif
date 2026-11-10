using System.Collections.Immutable;
using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotation
    {
        public Guid AutoMosaicId { get; init; }
        public ImmutableArray<Rect> AutoMosaicRegions { get; init; }
        public ImmutableArray<PrivacyCandidate> AutoMosaicCandidates { get; init; }
        public Rect AutoMosaicCandidateBounds { get; init; }
    }
}

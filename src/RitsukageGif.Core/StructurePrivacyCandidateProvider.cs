namespace RitsukageGif.Core
{
    public sealed class StructurePrivacyCandidateProvider : IPrivacyCandidateProvider
    {
        public string Id => "image-structure";

        public Task<IReadOnlyList<PrivacyCandidate>> DetectAsync(CapturedFrame frame,
            CancellationToken token = default)
        {
            PrivacyDetectionService.ValidateFrame(frame);
            token.ThrowIfCancellationRequested();
            return Task.Run<IReadOnlyList<PrivacyCandidate>>(() =>
            {
                var straight = frame.AlphaMode == CaptureAlphaMode.Straight;
                var pixels = straight ? frame.Pixels : CapturedFramePixels.ToStraightBgra(frame, token);
                var stride = straight ? frame.Stride : checked(frame.Width * 4);
                using var analysis =
                    StructureRegionDetector.Analyze(pixels.Span, frame.Width, frame.Height, stride, token);
                var coarse = analysis.Regions;
                var (regions, imageEligible, relationEligible) =
                    StructureRegionRefiner.Refine(pixels.Span, stride, analysis, token);
                var kinds = StructureCandidateClassifier.Classify(pixels.Span, stride, regions,
                    imageEligible, relationEligible, token);
                var result = new List<PrivacyCandidate>(regions.Count);
                for (var index = 0; index < regions.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    if (index >= coarse.Count && imageEligible[index] &&
                        kinds[index] != PrivacyCandidateKind.ImageBlock) continue;
                    var region = regions[index];
                    result.Add(new(Id, kinds[index], region, "", true)
                    {
                        AppearanceSignature = PrivacyAppearanceFingerprint.Create(pixels.Span, stride, region, token),
                    });
                }

                return result.AsReadOnly();
            }, token);
        }
    }
}

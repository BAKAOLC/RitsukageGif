namespace RitsukageGif.Core
{
    public sealed partial class PrivacyDetectionService
    {
        public Task<PrivacyDetectionResult> DetectAnnotationCandidatesAsync(ReadOnlyMemory<byte> encodedImage,
            string? languageTag = null, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var providers = _providers.Select(provider => provider is BarcodePrivacyCandidateProvider barcode
                ? barcode.ForAnnotations()
                : provider).ToList();
            if (providers.All(provider => provider.Id != "image-structure"))
                providers.Add(new StructurePrivacyCandidateProvider());
            return new PrivacyDetectionService(providers).DetectAsync(encodedImage, languageTag, token);
        }

        public static PrivacyDetectionService ForAnnotations(ITextRecognitionService text,
            IBarcodeRecognitionService barcode, string? languageTag = null)
        {
            return new([
                new OcrPrivacyCandidateProvider(text, languageTag),
                new BarcodePrivacyCandidateProvider(barcode, true),
                new StructurePrivacyCandidateProvider(),
            ]);
        }
    }
}

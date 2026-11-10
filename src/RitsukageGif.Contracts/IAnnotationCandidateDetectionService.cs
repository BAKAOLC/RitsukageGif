namespace RitsukageGif.Contracts
{
    public interface IAnnotationCandidateDetectionService
    {
        Task<PrivacyDetectionResult> DetectAnnotationCandidatesAsync(ReadOnlyMemory<byte> encodedImage,
            string? languageTag = null, CancellationToken token = default);
    }
}

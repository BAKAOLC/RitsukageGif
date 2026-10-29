namespace RitsukageGif.Contracts
{
    public interface IPrivacyDetectionService
    {
        Task<PrivacyDetectionResult> DetectAsync(ReadOnlyMemory<byte> encodedImage,
            string? languageTag = null, CancellationToken token = default);
    }
}

namespace RitsukageGif.Contracts
{
    public interface ITextRecognitionService
    {
        Task<OcrCapabilities> GetCapabilitiesAsync(CancellationToken token = default);

        Task<OcrRecognitionResult> RecognizeImageAsync(ReadOnlyMemory<byte> encoded,
            string? languageTag = null, CancellationToken token = default);

        // Input pixels use straight alpha. Result coordinates refer to the oriented input image.
        // ReSharper disable once UnusedMember.Global
        Task<OcrRecognitionResult> RecognizeBgraAsync(ReadOnlyMemory<byte> pixels, int width, int height,
            int stride, string? languageTag = null, CancellationToken token = default);
    }
}

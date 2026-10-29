namespace RitsukageGif.Contracts
{
    public interface IBarcodeRecognitionService
    {
        // ReSharper disable once UnusedMember.Global
        Task<IReadOnlyList<BarcodeRecognitionResult>> RecognizeAsync(CapturedFrame frame,
            CancellationToken token = default);

        Task<IReadOnlyList<BarcodeRecognitionResult>> RecognizeAsync(ReadOnlyMemory<byte> encodedImage,
            CancellationToken token = default);
    }
}

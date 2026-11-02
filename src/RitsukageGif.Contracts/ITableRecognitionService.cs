namespace RitsukageGif.Contracts
{
    public interface ITableRecognitionService
    {
        Task<RecognitionProviderCapabilities> GetCapabilitiesAsync(CancellationToken token = default);
        Task<TableRecognitionResult> RecognizeAsync(ReadOnlyMemory<byte> encoded, CancellationToken token = default);
    }
}

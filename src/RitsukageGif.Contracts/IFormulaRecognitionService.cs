namespace RitsukageGif.Contracts
{
    public interface IFormulaRecognitionService
    {
        Task<RecognitionProviderCapabilities> GetCapabilitiesAsync(CancellationToken token = default);
        Task<FormulaRecognitionResult> RecognizeAsync(ReadOnlyMemory<byte> encoded, CancellationToken token = default);
    }
}

namespace RitsukageGif.Contracts
{
    public interface IFormulaDocumentService
    {
        Task<ReadOnlyMemory<byte>> EncodeDocxAsync(IReadOnlyList<string> mathMl, CancellationToken token = default);
    }
}

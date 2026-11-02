namespace RitsukageGif.Contracts
{
    public interface ITableDataService
    {
        Task<TableRecognitionResult> ReadXlsxAsync(ReadOnlyMemory<byte> data, CancellationToken token = default);
    }
}

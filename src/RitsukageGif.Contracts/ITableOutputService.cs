namespace RitsukageGif.Contracts
{
    public interface ITableOutputService
    {
        Task<ReadOnlyMemory<byte>> EncodeAsync(TableRecognitionResult table, TableOutputFormat format,
            CancellationToken token = default);
    }
}

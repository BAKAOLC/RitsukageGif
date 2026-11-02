namespace RitsukageGif.Contracts
{
    public interface ITableEditService
    {
        Task<TableRecognitionResult?> EditAsync(TableRecognitionResult table, CancellationToken token = default);
    }
}

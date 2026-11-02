namespace RitsukageGif.Core
{
    public sealed class TableDataService : ITableDataService
    {
        public Task<TableRecognitionResult> ReadXlsxAsync(ReadOnlyMemory<byte> data, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (data.Length is <= 0 or > 64 * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(data));
            return Task.Run(() => TableWorkbookReader.ReadAsync(data, token), token);
        }
    }
}

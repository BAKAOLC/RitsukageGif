namespace RitsukageGif.Desktop
{
    internal interface ITablePrintRenderer
    {
        Task<IReadOnlyList<RasterPrintPage>> RenderAsync(TableRecognitionResult table, DocumentOutputOptions options,
            CancellationToken cancellationToken = default);
    }
}

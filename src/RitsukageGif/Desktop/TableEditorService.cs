using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class TableEditorService : ITableEditService
    {
        internal static TableEditorService Shared { get; } = new();

        public async Task<TableRecognitionResult?> EditAsync(TableRecognitionResult table,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return await Dispatcher.UIThread.InvokeAsync(() =>
                StructuredRecognitionWindow.ShowTableEditorAsync(table, token));
        }
    }
}

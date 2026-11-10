using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class StructuredRecognitionWindow
    {
        internal static async Task<TableRecognitionResult?> ShowTableEditorAsync(TableRecognitionResult table,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var window = new StructuredRecognitionWindow(default, false, null, FormulaCopyFormat.Latex,
                null, false, null, FormulaLatexFormat.DisplayDollar, null, initialTable: table,
                dialogPreferences: new() { Table = table.OutputStyle ?? TableOutputStyle.Default });
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            window.Closing += (_, e) =>
            {
                if (token.IsCancellationRequested) window._table.CommitEdit(false);
                else if (!window._table.CommitEdit()) e.Cancel = true;
            };
            await using var registration = token.Register(() => Dispatcher.UIThread.Post(window.Close));
            try
            {
                var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?
                    .Windows.FirstOrDefault(candidate => candidate is { IsActive: true, IsVisible: true });
                if (owner is null)
                {
                    window.Show();
                    await closed.Task;
                }
                else
                {
                    await window.ShowDialog(owner);
                }

                token.ThrowIfCancellationRequested();
                return window._table.Document is { } document ? window.WithTableOutputStyle(document.Table) : null;
            }
            finally
            {
                await window._lifetime.CancelAsync();
                await window._operation;
                window._lifetime.Dispose();
            }
        }
    }
}

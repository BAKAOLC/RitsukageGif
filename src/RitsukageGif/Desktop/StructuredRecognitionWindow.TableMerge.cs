using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class StructuredRecognitionWindow
    {
        private async Task MergeTableAsync()
        {
            var token = _lifetime.Token;
            if (_busy || token.IsCancellationRequested || !_table.HasSelection ||
                _table.Document is not { } document || !_table.CommitEdit()) return;
            var ranges = _table.SelectedRanges.ToArray();
            if (!document.CanMerge(ranges)) return;
            SetBusy(true);
            try
            {
                if (document.WouldMergeDiscardContent(ranges))
                {
                    var dialog = new TableMergeConfirmationDialog();
                    await using var registration = token.Register(() =>
                        Dispatcher.UIThread.Post(() => dialog.Close(false)));
                    if (!await dialog.ShowDialog<bool>(this)) return;
                    token.ThrowIfCancellationRequested();
                }

                Remember();
                document.Merge(ranges);
                _table.Refresh();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            finally
            {
                if (!token.IsCancellationRequested) SetBusy(false);
            }
        }
    }
}

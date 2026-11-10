namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private Task<bool> RunAutomationAsync(Func<Task> action)
        {
            return RunAutomationAsync(async () =>
            {
                await action();
                return true;
            });
        }

        private async Task<T> RunAutomationAsync<T>(Func<Task<T>> action)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_busy) throw new InvalidOperationException("The pin is busy.");
            _busy = true;
            try
            {
                return await action();
            }
            finally
            {
                _busy = false;
                _annotation?.SetExportState(false);
                if (_closed) _lifetime.Dispose();
            }
        }
    }
}

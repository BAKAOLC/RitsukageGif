namespace RitsukageGif.Desktop
{
    internal sealed class SelectionDialogScope(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose()
        {
            Interlocked.Exchange(ref _release, null)?.Invoke();
        }
    }
}

using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedRichResource : IDisposable
    {
        private Bitmap? _bitmap;
        private int _disposed;
        private Action? _release;

        internal PinnedRichResource(Bitmap? bitmap, string? error, Action? release)
        {
            _bitmap = bitmap;
            Error = error;
            _release = release;
        }

        internal Bitmap? Bitmap
        {
            get
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                return _bitmap;
            }
        }

        internal string? Error { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _bitmap = null;
            Interlocked.Exchange(ref _release, null)?.Invoke();
        }
    }
}

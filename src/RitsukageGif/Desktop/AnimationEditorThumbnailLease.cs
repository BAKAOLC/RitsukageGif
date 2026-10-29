using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed class AnimationEditorThumbnailLease(Bitmap bitmap, Action release) : IDisposable
    {
        private Action? _release = release;
        public Bitmap Bitmap { get; } = bitmap;

        public void Dispose()
        {
            Interlocked.Exchange(ref _release, null)?.Invoke();
        }
    }
}

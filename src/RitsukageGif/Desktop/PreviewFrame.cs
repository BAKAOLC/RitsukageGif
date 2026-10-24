using System.Buffers;

namespace RitsukageGif.Desktop
{
    internal sealed class PreviewFrame : IDisposable
    {
        private int _disposed;
        private int _references = 1;
        private readonly PreviewFrame _owner;

        public PreviewFrame(byte[] pixels, int width, int height, int delay)
        {
            Pixels = pixels;
            Width = width;
            Height = height;
            Delay = delay;
            _owner = this;
        }

        private PreviewFrame(PreviewFrame owner)
        {
            _owner = owner;
            Pixels = owner.Pixels;
            Width = owner.Width;
            Height = owner.Height;
            Delay = owner.Delay;
        }

        public byte[] Pixels { get; }
        public int Width { get; }
        public int Height { get; }
        public int Delay { get; }

        public PreviewFrame Retain()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var count = Volatile.Read(ref _owner._references);
            while (count != 0)
            {
                var observed = Interlocked.CompareExchange(ref _owner._references, count + 1, count);
                if (observed == count) return new PreviewFrame(_owner);
                count = observed;
            }

            throw new ObjectDisposedException(nameof(PreviewFrame));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 && Interlocked.Decrement(ref _owner._references) == 0)
                ArrayPool<byte>.Shared.Return(Pixels);
        }
    }
}

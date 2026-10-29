using System.Buffers;

namespace RitsukageGif.Desktop
{
    internal sealed class PreviewFrame : IDisposable
    {
        private readonly PreviewFrame _owner;
        private int _disposed;
        private int _references = 1;

        public PreviewFrame(byte[] pixels, int width, int height, int delay, byte[]? hdrPixels = null)
        {
            Pixels = pixels;
            HdrPixels = hdrPixels;
            Width = width;
            Height = height;
            Delay = delay;
            _owner = this;
        }

        private PreviewFrame(PreviewFrame owner)
        {
            _owner = owner;
            Pixels = owner.Pixels;
            HdrPixels = owner.HdrPixels;
            Width = owner.Width;
            Height = owner.Height;
            Delay = owner.Delay;
        }

        public byte[] Pixels { get; }
        public byte[]? HdrPixels { get; }
        public long StorageBytes => Pixels.LongLength + (HdrPixels?.LongLength ?? 0);
        public int Width { get; }
        public int Height { get; }
        public int Delay { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 && Interlocked.Decrement(ref _owner._references) == 0)
            {
                ArrayPool<byte>.Shared.Return(Pixels);
                if (HdrPixels is not null) ArrayPool<byte>.Shared.Return(HdrPixels);
            }
        }

        public PreviewFrame Retain()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var count = Volatile.Read(ref _owner._references);
            while (count != 0)
            {
                var observed = Interlocked.CompareExchange(ref _owner._references, count + 1, count);
                if (observed == count) return new(_owner);
                count = observed;
            }

            throw new ObjectDisposedException(nameof(PreviewFrame));
        }
    }
}

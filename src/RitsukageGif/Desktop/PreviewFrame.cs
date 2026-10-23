using System.Buffers;

namespace RitsukageGif.Desktop
{
    internal sealed class PreviewFrame(byte[] pixels, int width, int height, int delay) : IDisposable
    {
        private int _disposed;
        public byte[] Pixels { get; } = pixels;
        public int Width { get; } = width;
        public int Height { get; } = height;
        public int Delay { get; } = delay;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) ArrayPool<byte>.Shared.Return(Pixels);
        }
    }
}

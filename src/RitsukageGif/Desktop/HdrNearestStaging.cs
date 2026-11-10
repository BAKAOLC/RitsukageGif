using System.Buffers;

namespace RitsukageGif.Desktop
{
    internal sealed class HdrNearestStaging : IDisposable
    {
        private byte[]? _buffer;
        private int _width, _height;

        public void Dispose()
        {
            if (_buffer is null) return;
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }

        internal ReadOnlyMemory<byte> Sample(ReadOnlySpan<byte> source, int sourceWidth, int sourceHeight,
            int width, int height, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var length = checked(width * height * 8);
            if (_buffer is null || _width != width || _height != height)
            {
                var buffer = ArrayPool<byte>.Shared.Rent(length);
                if (_buffer is not null) ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = buffer;
                _width = width;
                _height = height;
            }

            var output = _buffer.AsSpan(0, length);
            for (var y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                var sourceY = (int)(((long)y * 2 + 1) * sourceHeight / ((long)height * 2));
                for (var x = 0; x < width; x++)
                {
                    if ((x & 1023) == 0) token.ThrowIfCancellationRequested();
                    var sourceX = (int)(((long)x * 2 + 1) * sourceWidth / ((long)width * 2));
                    source.Slice((sourceY * sourceWidth + sourceX) * 8, 8)
                        .CopyTo(output.Slice((y * width + x) * 8, 8));
                }
            }

            return _buffer.AsMemory(0, length);
        }
    }
}

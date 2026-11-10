using System.Buffers;
using Avalonia;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedInputUiCoverage : IDisposable
    {
        private RenderTargetBitmap? _bitmap;
        private byte[]? _pixels;
        private double _scaling;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _bitmap?.Dispose();
            if (_pixels is not null) ArrayPool<byte>.Shared.Return(_pixels);
            _pixels = null;
        }

        internal unsafe PinnedInputCoverage Render(Visual window, PixelSize size, double scaling,
            CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            if (_bitmap is null || _bitmap.PixelSize != size || !_scaling.Equals(scaling))
            {
                _bitmap?.Dispose();
                _bitmap = new(size, new(96 * scaling, 96 * scaling));
                _scaling = scaling;
            }

            var length = checked(size.Width * size.Height * 4);
            if (_pixels is null || _pixels.Length < length)
            {
                if (_pixels is not null) ArrayPool<byte>.Shared.Return(_pixels);
                _pixels = ArrayPool<byte>.Shared.Rent(length);
            }

            _bitmap.Render(window);
            fixed (byte* pixels = _pixels)
                _bitmap.CopyPixels(new(size), (nint)pixels, length, size.Width * 4);
            var result = new PinnedInputCoverage(size);
            for (var y = 0; y < size.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < size.Width; x++)
                    if (_pixels[(y * size.Width + x) * 4 + 3] != 0)
                        result.Include(x, y);
            }

            return result;
        }
    }
}

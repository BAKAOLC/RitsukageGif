using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed class SelectionDecorationPreview : IDisposable
    {
        private SelectionDecorationPreview(WriteableBitmap image, PixelPoint contentOffset)
        {
            Image = image;
            ContentOffset = contentOffset;
        }

        internal WriteableBitmap Image { get; }
        internal PixelPoint ContentOffset { get; }

        public void Dispose()
        {
            Image.Dispose();
        }

        internal static unsafe SelectionDecorationPreview Create(DecorationAlphaMask mask, uint color,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var bitmap = new WriteableBitmap(new(mask.Width, mask.Height), new(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul);
            try
            {
                using var target = bitmap.Lock();
                var alpha = mask.Alpha.Span;
                var opacity = color >> 24;
                var red = (color >> 16) & 255;
                var green = (color >> 8) & 255;
                var blue = color & 255;
                for (var y = 0; y < mask.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    var row = new Span<byte>((void*)(target.Address + y * target.RowBytes), mask.Width * 4);
                    for (var x = 0; x < mask.Width; x++)
                    {
                        var value = (alpha[y * mask.Width + x] * opacity + 127) / 255;
                        row[x * 4] = (byte)((blue * value + 127) / 255);
                        row[x * 4 + 1] = (byte)((green * value + 127) / 255);
                        row[x * 4 + 2] = (byte)((red * value + 127) / 255);
                        row[x * 4 + 3] = (byte)value;
                    }
                }

                return new(bitmap, mask.ContentOffset);
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }
    }
}

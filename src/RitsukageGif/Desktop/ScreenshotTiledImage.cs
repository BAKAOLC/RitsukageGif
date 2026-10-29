using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed class ScreenshotTiledImage : IImage, IDisposable
    {
        private const int TileSize = 1024;
        private const int MaximumTiles = 8;
        private readonly AlphaFormat _alphaFormat;
        private readonly byte[] _pixels;
        private readonly LinkedList<(int X, int Y)> _recent = [];
        private readonly Dictionary<(int X, int Y), WriteableBitmap> _tiles = [];
        private readonly int _width, _height;

        public ScreenshotTiledImage(int width, int height, byte[] pixels, AlphaFormat alphaFormat = AlphaFormat.Opaque)
        {
            if (width <= 0 || height <= 0 || pixels.LongLength != (long)width * height * 4)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"));
            _alphaFormat = alphaFormat;
            _width = width;
            _height = height;
            _pixels = pixels;
            VisibleRegion = new(0, 0, Math.Min(width, TileSize), Math.Min(height, TileSize));
        }

        public Rect VisibleRegion { get; set; }

        public void Dispose()
        {
            foreach (var bitmap in _tiles.Values) bitmap.Dispose();
            _tiles.Clear();
            _recent.Clear();
        }

        public Size Size => new(_width, _height);

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
            if (sourceRect.Width <= 0 || sourceRect.Height <= 0 || destRect.Width <= 0 || destRect.Height <= 0) return;
            var source = sourceRect.Intersect(VisibleRegion).Intersect(new(Size));
            if (source.Width <= 0 || source.Height <= 0) return;
            var scaleX = destRect.Width / sourceRect.Width;
            var scaleY = destRect.Height / sourceRect.Height;
            for (var y = (int)source.Y / TileSize; y <= ((int)Math.Ceiling(source.Bottom) - 1) / TileSize; y++)
            for (var x = (int)source.X / TileSize; x <= ((int)Math.Ceiling(source.Right) - 1) / TileSize; x++)
            {
                var bitmap = GetTile(x, y);
                var tile = new Rect(x * TileSize, y * TileSize, bitmap.PixelSize.Width, bitmap.PixelSize.Height);
                var intersection = source.Intersect(tile);
                var destination = new Rect(destRect.X + (intersection.X - sourceRect.X) * scaleX,
                    destRect.Y + (intersection.Y - sourceRect.Y) * scaleY, intersection.Width * scaleX,
                    intersection.Height * scaleY);
                context.DrawImage(bitmap,
                    new(intersection.X - tile.X, intersection.Y - tile.Y, intersection.Width, intersection.Height),
                    destination);
            }
        }

        public void ApplyPatch(PixelRect region, byte[] pixels)
        {
            if (region.IsEmpty || region.X < 0 || region.Y < 0 || region.Right > _width || region.Bottom > _height ||
                pixels.LongLength != (long)region.Width * region.Height * 4)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(region));
            for (var row = 0; row < region.Height; row++)
                Buffer.BlockCopy(pixels, row * region.Width * 4, _pixels,
                    ((region.Y + row) * _width + region.X) * 4, region.Width * 4);
            for (var y = region.Y / TileSize; y <= (region.Bottom - 1) / TileSize; y++)
            for (var x = region.X / TileSize; x <= (region.Right - 1) / TileSize; x++)
            {
                var key = (x, y);
                if (!_tiles.TryGetValue(key, out var bitmap)) continue;
                var left = Math.Max(region.X, x * TileSize);
                var top = Math.Max(region.Y, y * TileSize);
                var right = Math.Min(region.Right, x * TileSize + bitmap.PixelSize.Width);
                var bottom = Math.Min(region.Bottom, y * TileSize + bitmap.PixelSize.Height);
                using var buffer = bitmap.Lock();
                for (var row = top; row < bottom; row++)
                    Marshal.Copy(_pixels, (row * _width + left) * 4,
                        buffer.Address + (row - y * TileSize) * buffer.RowBytes + (left - x * TileSize) * 4,
                        (right - left) * 4);
            }
        }

        private WriteableBitmap GetTile(int x, int y)
        {
            var key = (x, y);
            if (_recent.Find(key) is { } node)
            {
                _recent.Remove(node);
                _recent.AddLast(node);
            }
            else
            {
                _recent.AddLast(key);
            }

            if (_tiles.TryGetValue(key, out var bitmap)) return bitmap;
            var width = Math.Min(TileSize, _width - x * TileSize);
            var height = Math.Min(TileSize, _height - y * TileSize);
            bitmap = new(new(width, height), new(96, 96), PixelFormat.Bgra8888, _alphaFormat);
            using (var buffer = bitmap.Lock())
                for (var row = 0; row < height; row++)
                    Marshal.Copy(_pixels, ((y * TileSize + row) * _width + x * TileSize) * 4,
                        buffer.Address + row * buffer.RowBytes, width * 4);
            _tiles.Add(key, bitmap);
            while (_tiles.Count > MaximumTiles && _recent.First is { } first)
            {
                _recent.RemoveFirst();
                if (_tiles.Remove(first.Value, out var expired)) expired.Dispose();
            }

            return bitmap;
        }
    }
}

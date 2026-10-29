using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private static byte[] ReadEffectPixels(ScreenshotAnnotation annotation, PixelRect region,
            CancellationToken token)
        {
            var pixels = new byte[checked(region.Width * region.Height * 4)];
            foreach (var tile in annotation.ImageTiles)
            {
                var bounds = new PixelRect((int)tile.Bounds.X, (int)tile.Bounds.Y,
                    (int)tile.Bounds.Width, (int)tile.Bounds.Height);
                var clipped = bounds.Intersect(region);
                if (clipped.IsEmpty) continue;
                using var bitmap = ((WriteableBitmap)tile.Image).Lock();
                for (var y = 0; y < clipped.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    Marshal.Copy(bitmap.Address + (clipped.Y + y - bounds.Y) * bitmap.RowBytes +
                                 (clipped.X - bounds.X) * 4, pixels,
                        ((clipped.Y + y - region.Y) * region.Width + clipped.X - region.X) * 4, clipped.Width * 4);
                }
            }

            return pixels;
        }

        private static async Task EraseImageLayerAsync(MagickImage source, PixelRect region,
            ScreenshotAnnotation annotation, CancellationToken token)
        {
            foreach (var tile in annotation.ImageTiles)
            {
                token.ThrowIfCancellationRequested();
                var bounds = new PixelRect((int)tile.Bounds.X, (int)tile.Bounds.Y,
                    (int)tile.Bounds.Width, (int)tile.Bounds.Height);
                var clipped = bounds.Intersect(region);
                if (clipped.IsEmpty) continue;
                var original = ReadEffectPixels(annotation, clipped, token);
                await Task.Run(() =>
                {
                    var coverage = CreateEraserCoverage(annotation, clipped, token);
                    using var collection = source.GetPixels();
                    var destination = collection.GetArea(clipped.X - region.X, clipped.Y - region.Y,
                                          (uint)clipped.Width, (uint)clipped.Height) ??
                                      throw new InvalidOperationException(UiText.Get("InvalidFrameBuffer"));
                    for (var y = 0; y < clipped.Height; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        for (var x = 0; x < clipped.Width; x++)
                        {
                            var index = y * clipped.Width + x;
                            var mask = coverage[index];
                            if (mask == 0) continue;
                            var offset = index * 4;
                            if (mask == ushort.MaxValue)
                            {
                                destination[offset] = original[offset + 2];
                                destination[offset + 1] = original[offset + 1];
                                destination[offset + 2] = original[offset];
                                destination[offset + 3] = original[offset + 3];
                                continue;
                            }

                            var amount = BitOperations.PopCount(mask);
                            var sourceWeight = original[offset + 3] * amount;
                            var destinationWeight = destination[offset + 3] * (16 - amount);
                            var weight = sourceWeight + destinationWeight;
                            for (var channel = 0; channel < 3; channel++)
                                destination[offset + channel] = weight > 0
                                    ? (byte)((original[offset + 2 - channel] * sourceWeight +
                                              destination[offset + channel] * destinationWeight + weight / 2) / weight)
                                    : (byte)0;
                            destination[offset + 3] = (byte)((weight + 8) / 16);
                        }
                    }

                    token.ThrowIfCancellationRequested();
                    collection.SetArea(clipped.X - region.X, clipped.Y - region.Y,
                        (uint)clipped.Width, (uint)clipped.Height, destination);
                }, token);
            }
        }

        private static async Task CopyMagnifierLayerAsync(MagickImage source, PixelRect region,
            ScreenshotAnnotation annotation, CancellationToken token)
        {
            var bounds = annotation.Bounds;
            var sampleRegion = new PixelRect((int)Math.Floor(bounds.X), (int)Math.Floor(bounds.Y),
                Math.Max(1, (int)Math.Ceiling(bounds.Right) - (int)Math.Floor(bounds.X)),
                Math.Max(1, (int)Math.Ceiling(bounds.Bottom) - (int)Math.Floor(bounds.Y)));
            var pixels = ReadEffectPixels(annotation, sampleRegion, token);
            var clipped = sampleRegion.Intersect(region);
            const int tileSize = 1024;
            for (var top = clipped.Y; top < clipped.Bottom; top += tileSize)
            for (var left = clipped.X; left < clipped.Right; left += tileSize)
            {
                var tile = new PixelRect(left, top, Math.Min(tileSize, clipped.Right - left),
                    Math.Min(tileSize, clipped.Bottom - top));
                await Task.Run(() =>
                {
                    var result = new byte[checked(tile.Width * tile.Height * 4)];
                    for (var y = 0; y < tile.Height; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        var sy = Math.Clamp((int)Math.Floor(bounds.Center.Y + (tile.Y + y + .5 - bounds.Center.Y) /
                            annotation.Properties.Magnification), sampleRegion.Y, sampleRegion.Bottom - 1);
                        for (var x = 0; x < tile.Width; x++)
                        {
                            var sx = Math.Clamp((int)Math.Floor(bounds.Center.X + (tile.X + x + .5 - bounds.Center.X) /
                                annotation.Properties.Magnification), sampleRegion.X, sampleRegion.Right - 1);
                            pixels.AsSpan(((sy - sampleRegion.Y) * sampleRegion.Width + sx - sampleRegion.X) * 4, 4)
                                .CopyTo(result.AsSpan((y * tile.Width + x) * 4, 4));
                        }
                    }

                    using var image = new MagickImage(result,
                        new PixelReadSettings((uint)tile.Width, (uint)tile.Height, StorageType.Char,
                            PixelMapping.BGRA));
                    source.Composite(image, tile.X - region.X, tile.Y - region.Y, CompositeOperator.Copy);
                }, token);
            }
        }
    }
}

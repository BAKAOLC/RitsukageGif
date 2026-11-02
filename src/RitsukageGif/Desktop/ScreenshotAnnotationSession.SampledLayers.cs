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
                    var coverage = CreatePixelCoverage(annotation, clipped, token);
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
    }
}

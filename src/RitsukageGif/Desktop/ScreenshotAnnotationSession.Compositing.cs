using Avalonia;
using Avalonia.Media.Imaging;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private async Task CompositeAnnotationLayerAsync(MagickImage source, PixelRect region,
            List<ScreenshotAnnotation> annotations, CancellationToken token, bool sampleImages = true)
        {
            if (annotations.Count == 0 || Selection.IsEmpty) return;
            var bounds = annotations.Select(RenderBounds).ToArray();
            const int tileSize = 1024;
            for (var y = 0; y < region.Height; y += tileSize)
            for (var x = 0; x < region.Width; x += tileSize)
            {
                token.ThrowIfCancellationRequested();
                var width = Math.Min(tileSize, region.Width - x);
                var height = Math.Min(tileSize, region.Height - y);
                var world = new Rect(region.X + x, region.Y + y, width, height);
                if (!bounds.Any(mark => mark.Intersects(world))) continue;
                using var tile = new RenderTargetBitmap(new(width, height), new(96, 96));
                using (var context = tile.CreateDrawingContext())
                {
                    using var translate = context.PushTransform(Matrix.CreateTranslation(-world.X, -world.Y));
                    using var clip = context.PushClip(new(AnnotationBounds.X, AnnotationBounds.Y,
                        AnnotationBounds.Width, AnnotationBounds.Height));
                    foreach (var annotation in annotations) Draw(context, annotation, sampleImages);
                }

                var left = x;
                var top = y;
                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    using var stream = new MemoryStream();
                    tile.Save(stream, PngBitmapEncoderOptions.Default);
                    using var overlay = new MagickImage(stream.ToArray());
                    using var overlayPixels = overlay.GetPixels();
                    var foreground = overlayPixels.ToByteArray(PixelMapping.BGRA)!;
                    using var target = source.GetPixels();
                    var destination = target.GetArea(left, top, (uint)width, (uint)height) ??
                                      throw new InvalidOperationException(UiText.Get("InvalidFrameBuffer"));
                    for (var row = 0; row < height; row++)
                    {
                        token.ThrowIfCancellationRequested();
                        for (var column = 0; column < width; column++)
                        {
                            var offset = (row * width + column) * 4;
                            var alpha = foreground[offset + 3];
                            if (alpha == 0) continue;
                            var behind = destination[offset + 3] * (255 - alpha);
                            var weight = alpha * 255 + behind;
                            for (var channel = 0; channel < 3; channel++)
                                destination[offset + channel] = (byte)((foreground[offset + 2 - channel] * alpha * 255 +
                                                                        destination[offset + channel] * behind +
                                                                        weight / 2) / weight);
                            destination[offset + 3] = (byte)((weight + 127) / 255);
                        }
                    }

                    token.ThrowIfCancellationRequested();
                    target.SetArea(left, top, (uint)width, (uint)height, destination);
                }, token);
            }
        }
    }
}

using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SceneCapture
    {
        private static void ApplyEffects(Layer layer, IReadOnlyList<PixelEffect> effects, int sourceWidth,
            int sourceHeight, PixelRect placement, CancellationToken token)
        {
            var bitmap = layer.Bitmap;
            var size = (sourceWidth, sourceHeight, placement.Width, placement.Height, bitmap.Width, bitmap.Height);
            if (!ReferenceEquals(layer.Effects, effects) || layer.EffectSize != size)
            {
                layer.MappedEffects = MapEffects(effects, sourceWidth, sourceHeight, placement.Width,
                    placement.Height, bitmap.Width, bitmap.Height);
                layer.EffectSize = size;
                layer.Effects = effects;
            }

            layer.Drawing.Flush();
            PixelEffectProcessor.Apply(bitmap.GetPixels(), bitmap.Width, bitmap.Height, bitmap.RowBytes,
                layer.MappedEffects, token: token, premultiplied: true);
            bitmap.NotifyPixelsChanged();
            layer.Drawing.Save();
            try
            {
                layer.Drawing.ClipPath(layer.Mask, SKClipOperation.Difference, true);
                layer.Drawing.Clear(SKColors.Transparent);
            }
            finally
            {
                layer.Drawing.Restore();
            }
        }

        private static PixelEffect[] MapEffects(IReadOnlyList<PixelEffect> effects, int sourceWidth,
            int sourceHeight, int outputWidth, int outputHeight, int width, int height)
        {
            var mapped = new List<PixelEffect>(effects.Count);
            foreach (var effect in effects)
            {
                var scaleX = (double)width /
                             Math.Max(1, effect.Space == PixelEffectSpace.Source ? sourceWidth : outputWidth);
                var scaleY = (double)height /
                             Math.Max(1, effect.Space == PixelEffectSpace.Source ? sourceHeight : outputHeight);
                PixelRect? region = null;
                if (effect.Region is { } rect)
                {
                    var x = (int)Math.Clamp(Math.Floor(rect.X * scaleX), 0, width);
                    var y = (int)Math.Clamp(Math.Floor(rect.Y * scaleY), 0, height);
                    var right = (int)Math.Clamp(Math.Ceiling(((long)rect.X + rect.Width) * scaleX), 0, width);
                    var bottom = (int)Math.Clamp(Math.Ceiling(((long)rect.Y + rect.Height) * scaleY), 0, height);
                    if (right <= x || bottom <= y) continue;
                    region = new PixelRect(x, y, right - x, bottom - y);
                }

                var scale = Math.Min(scaleX, scaleY);
                mapped.Add(effect with
                {
                    Region = region,
                    BlockSize = (int)Math.Clamp(Math.Round(effect.BlockSize * scale), 1, 4096),
                    Radius = (int)Math.Clamp(Math.Round(effect.Radius * scale), 1, 256),
                });
            }

            return [.. mapped];
        }
    }
}

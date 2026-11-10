using System.Numerics;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static class ImageTranslationHdrRenderer
    {
        internal static CapturedFrame Compose(CapturedFrame source,
            IReadOnlyList<ImageTranslationInpaintRegion> regions, byte[] textOverlay, CancellationToken token)
        {
            HdrAnnotationBlender.Validate(source);
            var output = source.Pixels[..checked(source.Stride * source.Height)].ToArray();
            foreach (var (bounds, coverage) in regions)
            {
                var pixels = new Vector4[checked(bounds.Width * bounds.Height)];
                for (var y = 0; y < bounds.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    var row = MemoryMarshal.Cast<byte, Half>(
                        output.AsSpan((bounds.Y + y) * source.Stride + bounds.X * 8, bounds.Width * 8));
                    for (var x = 0; x < bounds.Width; x++)
                    {
                        var alpha = source.AlphaMode == CaptureAlphaMode.Opaque ? 1 : (float)row[x * 4 + 3];
                        var scale = source.AlphaMode == CaptureAlphaMode.Premultiplied ? 1 : alpha;
                        pixels[y * bounds.Width + x] = new((float)row[x * 4] * scale,
                            (float)row[x * 4 + 1] * scale, (float)row[x * 4 + 2] * scale, alpha);
                    }
                }

                ImageInpainting.Restore(pixels, bounds.Width, bounds.Height, coverage, token: token);
                for (var y = 0; y < bounds.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    var row = MemoryMarshal.Cast<byte, Half>(
                        output.AsSpan((bounds.Y + y) * source.Stride + bounds.X * 8, bounds.Width * 8));
                    for (var x = 0; x < bounds.Width; x++)
                    {
                        var index = y * bounds.Width + x;
                        if (coverage[index] == 0) continue;
                        var pixel = pixels[index];
                        var scale = source.AlphaMode == CaptureAlphaMode.Premultiplied ? 1 :
                            pixel.W > 0 ? 1 / pixel.W : 0;
                        row[x * 4] = (Half)Math.Clamp(pixel.X * scale, -65504, 65504);
                        row[x * 4 + 1] = (Half)Math.Clamp(pixel.Y * scale, -65504, 65504);
                        row[x * 4 + 2] = (Half)Math.Clamp(pixel.Z * scale, -65504, 65504);
                        row[x * 4 + 3] = (Half)(source.AlphaMode == CaptureAlphaMode.Opaque ? 1 : pixel.W);
                    }
                }
            }

            HdrAnnotationBlender.Blend(output, source, new(0, 0, source.Width, source.Height),
                textOverlay, source.Width * 4, CaptureAlphaMode.Straight, token);
            return source with { Pixels = output };
        }
    }
}

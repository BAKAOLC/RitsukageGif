namespace RitsukageGif.Core
{
    public static unsafe partial class PixelEffectProcessor
    {
        private static void ApplyPointRow(byte* pixel, int width, PixelEffect effect, bool premultiplied)
        {
            for (var x = 0; x < width; x++, pixel += 4)
            {
                Read(pixel, premultiplied, out var b, out var g, out var r, out var a);
                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                switch (effect.Kind)
                {
                    case PixelEffectKind.Fill:
                        b = effect.Color & 255;
                        g = (effect.Color >> 8) & 255;
                        r = (effect.Color >> 16) & 255;
                        a = effect.Color >> 24;
                        break;
                    case PixelEffectKind.Grayscale:
                        b = g = r = (29 * b + 150 * g + 77 * r) / 256;
                        break;
                    case PixelEffectKind.Invert:
                        b = 255 - b;
                        g = 255 - g;
                        r = 255 - r;
                        break;
                    case PixelEffectKind.BrightnessContrast:
                        b = Adjust(b, effect);
                        g = Adjust(g, effect);
                        r = Adjust(r, effect);
                        break;
                    default: throw new ArgumentOutOfRangeException(nameof(effect));
                }

                Write(pixel, b, g, r, a, effect.Amount, premultiplied);
            }
        }

        private static void ApplyFullStrengthPointEffect(nint pixels, int stride, PixelRect area, PixelEffect effect,
            ParallelOptions parallel, bool premultiplied)
        {
            var adjustments = stackalloc byte[256];
            if (effect.Kind == PixelEffectKind.BrightnessContrast)
                for (var value = 0; value < 256; value++)
                    adjustments[value] = Byte(Adjust(value, effect));
            // Rows completes all workers before this stack buffer leaves scope.
            var adjustmentTable = (nint)adjustments;
            Rows(area.Height, area.Width, parallel, row =>
            {
                var pixel = (byte*)pixels + (long)(area.Y + row) * stride + area.X * 4L;
                for (var x = 0; x < area.Width; x++)
                    if (pixel[x * 4 + 3] != 255)
                    {
                        ApplyPointRow(pixel, area.Width, effect, premultiplied);
                        return;
                    }

                for (var x = 0; x < area.Width; x++, pixel += 4)
                    // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                    switch (effect.Kind)
                    {
                        case PixelEffectKind.Grayscale:
                            var sum = 29 * pixel[0] + 150 * pixel[1] + 77 * pixel[2];
                            // Match Math.Round's midpoint-to-even rule for the exact /256 result.
                            var gray = (byte)((sum + 127 + ((sum >> 8) & 1)) >> 8);
                            pixel[0] = pixel[1] = pixel[2] = gray;
                            break;
                        case PixelEffectKind.Invert:
                            pixel[0] = (byte)(255 - pixel[0]);
                            pixel[1] = (byte)(255 - pixel[1]);
                            pixel[2] = (byte)(255 - pixel[2]);
                            break;
                        case PixelEffectKind.BrightnessContrast:
                            var table = (byte*)adjustmentTable;
                            pixel[0] = table[pixel[0]];
                            pixel[1] = table[pixel[1]];
                            pixel[2] = table[pixel[2]];
                            break;
                        default: throw new ArgumentOutOfRangeException(nameof(effect));
                    }
            });
        }
    }
}

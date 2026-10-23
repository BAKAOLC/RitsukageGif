namespace RitsukageGif.Core
{
    public static unsafe partial class PixelEffectProcessor
    {
        public static void Apply(byte[] pixels, int width, int height, IReadOnlyList<PixelEffect> effects,
            CancellationToken token = default, int maxDegreeOfParallelism = -1, bool premultiplied = false)
        {
            ArgumentNullException.ThrowIfNull(pixels);
            if (width <= 0 || height <= 0 || (long)width * height * 4 != pixels.Length)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"));
            fixed (byte* pointer = pixels)
                Apply((nint)pointer, width, height, checked(width * 4), effects, token, maxDegreeOfParallelism,
                    premultiplied);
        }

        public static void Apply(nint pixels, int width, int height, int stride, IReadOnlyList<PixelEffect> effects,
            CancellationToken token = default, int maxDegreeOfParallelism = -1, bool premultiplied = false)
        {
            ArgumentNullException.ThrowIfNull(effects);
            if (pixels == 0 || width <= 0 || height <= 0 || (long)width * height > 64L * 1024 * 1024 ||
                stride < (long)width * 4 || maxDegreeOfParallelism == 0 || maxDegreeOfParallelism < -1 ||
                effects.Count > 64)
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"));
            foreach (var effect in effects)
            {
                ArgumentNullException.ThrowIfNull(effect);
                effect.Validate();
            }

            var parallel = new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = maxDegreeOfParallelism == -1
                    ? Math.Min(32, Environment.ProcessorCount)
                    : maxDegreeOfParallelism
            };
            foreach (var effect in effects)
            {
                token.ThrowIfCancellationRequested();
                if (effect.Amount == 0) continue;
                var area = Clip(effect.Region ?? new PixelRect(0, 0, width, height), width, height);
                if (area.IsEmpty) continue;
                if (effect.Kind == PixelEffectKind.Blur)
                    Blur(pixels, stride, area, effect, parallel, premultiplied);
                else if (effect.Kind == PixelEffectKind.Mosaic)
                    Mosaic(pixels, stride, area, effect, parallel, premultiplied);
                else
                    Rows(area.Height, area.Width, parallel, row =>
                    {
                        var pixel = (byte*)pixels + (long)(area.Y + row) * stride + area.X * 4L;
                        for (var x = 0; x < area.Width; x++, pixel += 4)
                        {
                            Read(pixel, premultiplied, out var b, out var g, out var r, out var a);
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
                            }

                            Write(pixel, b, g, r, a, effect.Amount, premultiplied);
                        }
                    });
            }
        }

        private static double Adjust(double value, PixelEffect effect) =>
            Math.Clamp((value - 127.5) * effect.Contrast + 127.5 + effect.Brightness * 255, 0, 255);

        private static PixelRect Clip(PixelRect rectangle, int width, int height)
        {
            var left = Math.Clamp((long)rectangle.X, 0, width);
            var top = Math.Clamp((long)rectangle.Y, 0, height);
            var right = Math.Clamp((long)rectangle.X + rectangle.Width, 0, width);
            var bottom = Math.Clamp((long)rectangle.Y + rectangle.Height, 0, height);
            return new PixelRect((int)left, (int)top, (int)Math.Max(0, right - left), (int)Math.Max(0, bottom - top));
        }

        private static void Rows(int count, long width, ParallelOptions options, Action<int> action)
        {
            if (count * width >= 16384 && options.MaxDegreeOfParallelism != 1)
                Parallel.For(0, count, options, action);
            else
                for (var index = 0; index < count; index++)
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    action(index);
                }
        }

        private static void Read(byte* pixel, bool premultiplied, out double b, out double g, out double r,
            out double a)
        {
            a = pixel[3];
            var factor = premultiplied ? a > 0 ? 255 / a : 0 : 1;
            b = Math.Min(255, pixel[0] * factor);
            g = Math.Min(255, pixel[1] * factor);
            r = Math.Min(255, pixel[2] * factor);
        }

        private static void Write(byte* pixel, double b, double g, double r, double a, double amount,
            bool premultiplied)
        {
            var oldAlpha = pixel[3];
            var alpha = oldAlpha + (a - oldAlpha) * amount;
            var oldFactor = premultiplied ? 1 : oldAlpha / 255.0;
            var newFactor = a / 255;
            var outputFactor = premultiplied ? 1 : alpha > 0 ? 255 / alpha : 0;
            pixel[0] = Byte((pixel[0] * oldFactor * (1 - amount) + b * newFactor * amount) * outputFactor);
            pixel[1] = Byte((pixel[1] * oldFactor * (1 - amount) + g * newFactor * amount) * outputFactor);
            pixel[2] = Byte((pixel[2] * oldFactor * (1 - amount) + r * newFactor * amount) * outputFactor);
            pixel[3] = Byte(alpha);
        }

        private static byte Byte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    }
}

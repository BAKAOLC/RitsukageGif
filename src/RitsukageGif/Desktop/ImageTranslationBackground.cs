using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed record ImageTranslationBackground(byte[] Pixels, int Width, int Height, uint[] Foreground)
    {
        internal IReadOnlyList<ImageTranslationInpaintRegion> InpaintRegions { get; private init; } = [];

        internal static unsafe ImageTranslationBackground Create(ImageTranslationDocument document,
            CancellationToken token, bool retainMasks = false)
        {
            using var stream = new MemoryStream(document.SourcePng.ToArray(), false);
            using var codec = SKCodec.Create(stream) ??
                              throw new InvalidDataException("The translation source image is invalid.");
            if (codec.Info.Width != document.Width || codec.Info.Height != document.Height)
                throw new InvalidDataException("Recognition coordinates do not match the source image.");
            using var srgb = SKColorSpace.CreateSrgb();
            var pixels = new byte[checked(document.Width * document.Height * 4)];
            var info = new SKImageInfo(document.Width, document.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul,
                srgb);
            fixed (byte* address = pixels)
                if (codec.GetPixels(info, (IntPtr)address) != SKCodecResult.Success)
                    throw new InvalidDataException("The translation source image could not be decoded.");
            var foreground = new uint[document.Blocks.Count];
            var regions = retainMasks ? new List<ImageTranslationInpaintRegion>() : null;
            foreach (var block in document.Blocks)
            {
                token.ThrowIfCancellationRequested();
                var words = block.Words.Count == 0 ? [block.Bounds] : block.Words;
                const int padding = 6;
                var left = Math.Max(0, (int)Math.Floor(words.Min(word => word.X)) - padding);
                var top = Math.Max(0, (int)Math.Floor(words.Min(word => word.Y)) - padding);
                var right = Math.Min(document.Width,
                    (int)Math.Ceiling(words.Max(word => word.X + word.Width)) + padding);
                var bottom = Math.Min(document.Height,
                    (int)Math.Ceiling(words.Max(word => word.Y + word.Height)) + padding);
                var width = right - left;
                var height = bottom - top;
                var region = new byte[checked(width * height * 4)];
                for (var row = 0; row < height; row++)
                    pixels.AsSpan(((top + row) * document.Width + left) * 4, width * 4)
                        .CopyTo(region.AsSpan(row * width * 4));
                var background = BackgroundColor(region, width, height);
                var coverage = new ushort[checked(width * height)];
                var histogram = new int[256];
                foreach (var word in words)
                {
                    var bounds = WordRect(word, left, top, width, height);
                    for (var y = bounds.Y; y < bounds.Bottom; y++)
                    for (var x = bounds.X; x < bounds.Right; x++)
                        histogram[Distance(region, (y * width + x) * 4, background)]++;
                }

                var threshold = Math.Max(2, Threshold(histogram));
                var maximum = 255;
                while (maximum > 0 && histogram[maximum] == 0) maximum--;
                long red = 0, green = 0, blue = 0, count = 0;
                foreach (var word in words)
                {
                    var bounds = WordRect(word, left, top, width, height);
                    for (var y = bounds.Y; y < bounds.Bottom; y++)
                    for (var x = bounds.X; x < bounds.Right; x++)
                    {
                        var offset = (y * width + x) * 4;
                        var distance = Distance(region, offset, background);
                        if (distance <= threshold) continue;
                        coverage[y * width + x] = ushort.MaxValue;
                        if (distance < threshold + (maximum - threshold) / 2) continue;
                        blue += region[offset];
                        green += region[offset + 1];
                        red += region[offset + 2];
                        count++;
                    }
                }

                if (count == 0)
                    throw new InvalidDataException("The text could not be separated from its image background.");
                foreground[block.Id] = 0xff000000 | ((uint)(red / count) << 16) | ((uint)(green / count) << 8) |
                                       (uint)(blue / count);
                var ink = (ushort[])coverage.Clone();
                foreach (var word in words)
                {
                    var bounds = WordRect(word, left, top, width, height);
                    for (var y = bounds.Y; y < bounds.Bottom; y++)
                    for (var x = bounds.X; x < bounds.Right; x++)
                    {
                        if (ink[y * width + x] == 0) continue;
                        for (var dy = Math.Max(bounds.Y, y - 1); dy <= Math.Min(bounds.Bottom - 1, y + 1); dy++)
                        for (var dx = Math.Max(bounds.X, x - 1); dx <= Math.Min(bounds.Right - 1, x + 1); dx++)
                            coverage[dy * width + dx] = ushort.MaxValue;
                    }
                }

                regions?.Add(new(new(left, top, width, height), coverage));
                ImageInpainting.RestoreBgra(region, width, height, coverage, token: token);
                for (var row = 0; row < height; row++)
                for (var column = 0; column < width; column++)
                {
                    if (coverage[row * width + column] == 0) continue;
                    region.AsSpan((row * width + column) * 4, 4)
                        .CopyTo(pixels.AsSpan(((top + row) * document.Width + left + column) * 4));
                }
            }

            return new(pixels, document.Width, document.Height, foreground) { InpaintRegions = regions ?? [] };
        }

        private static PixelRect WordRect(OcrBounds word, int left, int top, int width, int height)
        {
            var x = Math.Clamp((int)Math.Floor(word.X) - left, 0, width);
            var y = Math.Clamp((int)Math.Floor(word.Y) - top, 0, height);
            var right = Math.Clamp((int)Math.Ceiling(word.X + word.Width) - left, x, width);
            var bottom = Math.Clamp((int)Math.Ceiling(word.Y + word.Height) - top, y, height);
            return new(x, y, right - x, bottom - y);
        }

        private static (byte Blue, byte Green, byte Red) BackgroundColor(byte[] pixels, int width, int height)
        {
            var blue = new List<byte>();
            var green = new List<byte>();
            var red = new List<byte>();
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (x > 1 && y > 1 && x < width - 2 && y < height - 2) continue;
                var offset = (y * width + x) * 4;
                blue.Add(pixels[offset]);
                green.Add(pixels[offset + 1]);
                red.Add(pixels[offset + 2]);
            }

            blue.Sort();
            green.Sort();
            red.Sort();
            return (blue[blue.Count / 2], green[green.Count / 2], red[red.Count / 2]);
        }

        private static int Distance(byte[] pixels, int offset, (byte Blue, byte Green, byte Red) background)
        {
            return (Math.Abs(pixels[offset] - background.Blue) + Math.Abs(pixels[offset + 1] - background.Green) +
                    Math.Abs(pixels[offset + 2] - background.Red)) / 3;
        }

        private static int Threshold(int[] histogram)
        {
            var total = histogram.Sum();
            long sum = 0;
            for (var index = 0; index < histogram.Length; index++) sum += (long)index * histogram[index];
            long foreground = 0;
            var background = 0;
            var threshold = 0;
            var maximum = 0d;
            for (var index = 0; index < histogram.Length; index++)
            {
                background += histogram[index];
                foreground += (long)index * histogram[index];
                var remaining = total - background;
                if (background == 0 || remaining == 0) continue;
                var difference = (double)foreground / background - (double)(sum - foreground) / remaining;
                var variance = (double)background * remaining * difference * difference;
                if (variance <= maximum) continue;
                maximum = variance;
                threshold = index;
            }

            return threshold;
        }
    }
}

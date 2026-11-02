using ImageMagick;

namespace RitsukageGif.Core
{
    internal sealed class GrayImage(byte[] pixels, int width, int height)
    {
        internal byte[] Pixels { get; } = pixels;
        internal int Width { get; } = width;
        internal int Height { get; } = height;

        internal static GrayImage FromImage(MagickImage source)
        {
            using var image = source.Clone();
            image.ColorSpace = ColorSpace.Gray;
            image.Depth = 8;
            return new(image.ToByteArray(MagickFormat.Gray), checked((int)image.Width), checked((int)image.Height));
        }

        internal GrayImage Crop(int x, int y, int width, int height)
        {
            if (x == 0 && y == 0 && width == Width && height == Height) return this;
            var pixels = new byte[checked(width * height)];
            for (var row = 0; row < height; row++)
                Pixels.AsSpan((y + row) * Width + x, width).CopyTo(pixels.AsSpan(row * width));
            return new(pixels, width, height);
        }

        internal GrayImage Resize(int width, int height, bool enlarge)
        {
            using var image = new MagickImage(Pixels,
                new PixelReadSettings((uint)Width, (uint)Height, StorageType.Char, "I"));
            image.FilterType = enlarge ? FilterType.Triangle : FilterType.Lanczos;
            image.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
            image.Depth = 8;
            return new(image.ToByteArray(MagickFormat.Gray), width, height);
        }

        internal GrayImage Fit()
        {
            var ratio = Math.Max(Width / 672d, Height / 192d);
            var image = ratio > 1
                ? Resize(Math.Max(1, (int)(Width / ratio)), Math.Max(1, (int)(Height / ratio)), false)
                : this;
            var width = Math.Max(32, image.Width);
            var height = Math.Max(32, image.Height);
            if (width == image.Width && height == image.Height) return image;
            var pixels = new byte[checked(width * height)];
            Array.Fill(pixels, (byte)255);
            for (var y = 0; y < image.Height; y++)
                image.Pixels.AsSpan(y * image.Width, image.Width).CopyTo(pixels.AsSpan(y * width));
            return new(pixels, width, height);
        }

        internal GrayImage CropAndPad()
        {
            var minimum = Pixels.Min();
            var maximum = Pixels.Max();
            if (minimum == maximum)
                throw new InvalidOperationException("The formula image contains no visible content.");
            var normalized = new byte[Pixels.Length];
            long sum = 0;
            for (var index = 0; index < normalized.Length; index++)
            {
                normalized[index] = (byte)((Pixels[index] - minimum) * 255d / (maximum - minimum));
                sum += normalized[index];
            }

            var invert = sum / (double)normalized.Length <= 128;
            var left = Width;
            var top = Height;
            var right = -1;
            var bottom = -1;
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var index = y * Width + x;
                if (invert) normalized[index] = (byte)(255 - normalized[index]);
                if (normalized[index] >= 128) continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }

            if (right < left) throw new InvalidOperationException("The formula image contains no visible content.");
            var width = (right - left + 32) / 32 * 32;
            var height = (bottom - top + 32) / 32 * 32;
            var pixels = new byte[checked(width * height)];
            Array.Fill(pixels, (byte)255);
            for (var y = top; y <= bottom; y++)
                normalized.AsSpan(y * Width + left, right - left + 1).CopyTo(pixels.AsSpan((y - top) * width));
            return new(pixels, width, height);
        }

        internal float[] Normalize()
        {
            var tensor = new float[Pixels.Length];
            for (var index = 0; index < tensor.Length; index++)
                tensor[index] = (Pixels[index] / 255f - .7931f) / .1738f;
            return tensor;
        }
    }
}

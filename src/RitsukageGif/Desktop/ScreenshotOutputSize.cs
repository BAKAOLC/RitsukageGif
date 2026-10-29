using Avalonia;

namespace RitsukageGif.Desktop
{
    public sealed record ScreenshotOutputSize
    {
        public ScreenshotOutputSizeMode Mode { get; set; }
        public double Percent { get; set; } = 100;
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1080;

        public ScreenshotOutputSize Normalize()
        {
            return new()
            {
                Mode = Enum.IsDefined(Mode) ? Mode : ScreenshotOutputSizeMode.Original,
                Percent = double.IsFinite(Percent) && Percent is > 0 and <= 10000 ? Percent : 100,
                Width = Math.Clamp(Width, 1, 128 * 1024 * 1024),
                Height = Math.Clamp(Height, 1, 128 * 1024 * 1024),
            };
        }

        public PixelSize Resolve(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            double outputWidth;
            double outputHeight;
            switch (Mode)
            {
                case ScreenshotOutputSizeMode.Original:
                    outputWidth = width;
                    outputHeight = height;
                    break;
                case ScreenshotOutputSizeMode.Percent when double.IsFinite(Percent) && Percent is > 0 and <= 10000:
                    outputWidth = width * Percent / 100;
                    outputHeight = height * Percent / 100;
                    break;
                case ScreenshotOutputSizeMode.Fit when Width > 0 && Height > 0:
                    var scale = Math.Min((double)Width / width, (double)Height / height);
                    outputWidth = width * scale;
                    outputHeight = height * scale;
                    break;
                case ScreenshotOutputSizeMode.Exact when Width > 0 && Height > 0:
                    outputWidth = Width;
                    outputHeight = Height;
                    break;
                default:
                    throw new ArgumentException("Invalid screenshot output dimensions.");
            }

            outputWidth = Math.Max(1, Math.Round(outputWidth, MidpointRounding.AwayFromZero));
            outputHeight = Math.Max(1, Math.Round(outputHeight, MidpointRounding.AwayFromZero));
            if (outputWidth > int.MaxValue || outputHeight > int.MaxValue ||
                outputWidth * outputHeight > 128L * 1024 * 1024)
                throw new ArgumentException("The screenshot output exceeds the pixel limit.");
            return new((int)outputWidth, (int)outputHeight);
        }
    }
}

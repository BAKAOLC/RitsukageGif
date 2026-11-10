using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal static class AnnotationContrast
    {
        internal static Color Foreground(Color background)
        {
            var luminance = .2126 * Linear(background.R) + .7152 * Linear(background.G) + .0722 * Linear(background.B);
            var channel = (byte)((luminance + .05) / .05 > 1.05 / (luminance + .05) ? 0 : 255);
            return new(background.A, channel, channel, channel);
        }

        private static double Linear(byte channel)
        {
            var encoded = channel / 255d;
            return encoded <= .04045 ? encoded / 12.92 : Math.Pow((encoded + .055) / 1.055, 2.4);
        }
    }
}

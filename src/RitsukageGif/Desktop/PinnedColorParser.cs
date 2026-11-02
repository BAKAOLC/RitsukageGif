using System.Globalization;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal static class PinnedColorParser
    {
        public static bool TryParse(string? text, out Color color)
        {
            color = default;
            if (text is null) return false;
            var value = text.Trim();
            if (value.Length is 0 or > 128) return false;
            if (value[0] == '#')
            {
                var hex = value.AsSpan(1);
                if (hex.Length is not (3 or 6 or 8) ||
                    !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var number))
                    return false;
                color = hex.Length switch
                {
                    3 => Color.FromRgb((byte)(((number >> 8) & 15) * 17),
                        (byte)(((number >> 4) & 15) * 17), (byte)((number & 15) * 17)),
                    6 => Color.FromRgb((byte)(number >> 16), (byte)(number >> 8), (byte)number),
                    _ => Color.FromArgb((byte)number, (byte)(number >> 24), (byte)(number >> 16), (byte)(number >> 8)),
                };
                return true;
            }

            var open = value.IndexOf('(');
            var function = open < 0 ? "" : value[..open].Trim().ToLowerInvariant();
            if (open >= 0 && (!value.EndsWith(')') || function is not ("rgb" or "rgba" or "hsl" or "hsla")))
                return false;
            var body = open < 0 ? value : value[(open + 1)..^1];
            var parts = body.Split([' ', ',', '\t'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var expected = function is "rgba" or "hsla" ? 4 : 3;
            if (parts.Length != expected && !(function.Length == 0 && parts.Length == 4))
                return open < 0 && value.All(char.IsLetter) && Color.TryParse(value, out color);

            var normalized = function.Length == 0 && parts.Any(part => part.Contains('.') ||
                                                                       part.Contains('e',
                                                                           StringComparison.OrdinalIgnoreCase));
            Span<double> channels = stackalloc double[4];
            channels[3] = 1;
            var hsl = function is "hsl" or "hsla";
            for (var index = 0; index < parts.Length; index++)
            {
                var part = parts[index];
                var percent = part.EndsWith('%');
                var number = percent ? part.AsSpan(0, part.Length - 1) : part.AsSpan();
                if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var channel) ||
                    !double.IsFinite(channel)) return false;
                if (hsl && index == 0)
                {
                    if (percent) return false;
                    channels[index] = (channel % 360 + 360) % 360;
                    continue;
                }

                var scale = percent ? 100 :
                    hsl || normalized ||
                    (index == 3 && function.Length > 0) ? 1 : 255;
                if (channel < 0 || channel > scale) return false;
                channels[index] = channel / scale;
            }

            if (hsl)
            {
                var chroma = (1 - Math.Abs(2 * channels[2] - 1)) * channels[1];
                var x = chroma * (1 - Math.Abs(channels[0] / 60 % 2 - 1));
                var offset = channels[2] - chroma / 2;
                var sector = (int)(channels[0] / 60);
                var (red, green, blue) = sector switch
                {
                    0 => (chroma, x, 0d), 1 => (x, chroma, 0d), 2 => (0d, chroma, x),
                    3 => (0d, x, chroma), 4 => (x, 0d, chroma), _ => (chroma, 0d, x),
                };
                channels[0] = red + offset;
                channels[1] = green + offset;
                channels[2] = blue + offset;
            }

            color = Color.FromArgb(Byte(channels[3]), Byte(channels[0]), Byte(channels[1]), Byte(channels[2]));
            return true;
        }

        private static byte Byte(double value)
        {
            return (byte)Math.Clamp((int)Math.Round(value * 255, MidpointRounding.AwayFromZero), 0, 255);
        }
    }
}

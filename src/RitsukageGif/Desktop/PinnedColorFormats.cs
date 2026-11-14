using System.Globalization;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedColorFormats(Color color)
    {
        private readonly SampleColorValues _values = new(new(color.R, color.G, color.B, color.A, default, default));

        // QColor represents an undefined hue as -1, normalized by getColorDesc to 0.
        private int Hue => Whole(Whole(_values.Hue * 100) / 100d);

        internal string Format(PinnedColorFormat format, bool includeName = false, int width = 0)
        {
            var opaque = color.A == byte.MaxValue;
            var (name, value) = format switch
            {
                PinnedColorFormat.Rgb => (opaque ? "RGB: " : "RGBA: ",
                    Join(Integer(color.R), Integer(color.G), Integer(color.B),
                        opaque ? null : Fraction(color.A / 255d))),
                PinnedColorFormat.RgbFraction => (opaque ? "RGB: " : "RGBA: ",
                    Join(Fraction(_values.Red), Fraction(_values.Green), Fraction(_values.Blue),
                        opaque ? null : Fraction(color.A / 255d))),
                PinnedColorFormat.Hsv => (opaque ? "HSV: " : " HSV: ",
                    Join(Integer(Hue), Integer(Percentage(_values.HsvSaturation)),
                        Integer(Percentage(_values.Maximum)))),
                PinnedColorFormat.Hsl => (opaque ? "HSL: " : " HSL: ",
                    Join(Integer(Hue), Integer(Percentage(_values.HslSaturation)),
                        Integer(Percentage(_values.Lightness)))),
                PinnedColorFormat.Cmyk => ("CMYK: ",
                    Join(Integer(CmykChannel(_values.Red)), Integer(CmykChannel(_values.Green)),
                        Integer(CmykChannel(_values.Blue)), Integer(ToColorByte(1 - _values.Maximum)))),
                PinnedColorFormat.Hex or PinnedColorFormat.HashHex => (opaque ? "HEX:   " : " HEX:   ",
                    (format == PinnedColorFormat.HashHex ? "#" : "") +
                    (opaque
                        ? FormattableString.Invariant($"{color.R:X2}{color.G:X2}{color.B:X2}")
                        : FormattableString.Invariant($"{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}"))),
                _ => throw new ArgumentOutOfRangeException(nameof(format)),
            };
            return includeName ? name + value : value;

            string Integer(int component)
            {
                return component.ToString(CultureInfo.InvariantCulture).PadLeft(width);
            }

            string Fraction(double component)
            {
                return component.ToString("F3", CultureInfo.InvariantCulture).PadLeft(width);
            }
        }

        private int CmykChannel(double component)
        {
            return _values.Maximum == 0 ? 0 : ToColorByte(1 - component / _values.Maximum);
        }

        private static int ToColorByte(double channel)
        {
            return Whole(Whole(channel * ushort.MaxValue) / 257d);
        }

        private static int Percentage(double channel)
        {
            return Whole(Whole(channel * ushort.MaxValue) * 100d / ushort.MaxValue);
        }

        private static int Whole(double component)
        {
            return (int)Math.Round(component, MidpointRounding.AwayFromZero);
        }

        private static string Join(string first, string second, string third, string? fourth = null)
        {
            return fourth is null
                ? string.Concat(first, ",", second, ",", third)
                : first + "," + second + "," + third + "," + fourth;
        }
    }
}

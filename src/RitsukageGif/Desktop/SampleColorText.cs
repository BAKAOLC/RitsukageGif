using System.Globalization;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal static class SampleColorText
    {
        internal static SampleColorFormat Next(SampleColorFormat format)
        {
            return format switch
            {
                SampleColorFormat.Rgb => SampleColorFormat.Hex,
                SampleColorFormat.Hex => SampleColorFormat.Hsv,
                SampleColorFormat.Hsv => SampleColorFormat.Hsl,
                _ => SampleColorFormat.Rgb,
            };
        }

        internal static string Format(Color color, SampleColorFormat format)
        {
            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (format)
            {
                case SampleColorFormat.Hex: return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                case SampleColorFormat.Rgb: return $"rgb({color.R}, {color.G}, {color.B})";
            }

            var red = color.R / 255d;
            var green = color.G / 255d;
            var blue = color.B / 255d;
            var max = Math.Max(red, Math.Max(green, blue));
            var min = Math.Min(red, Math.Min(green, blue));
            var light = (max + min) / 2;
            var delta = max - min;
            var saturation = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * light - 1));
            var hue = delta == 0 ? 0 :
                color.R >= color.G && color.R >= color.B ? (green - blue) / delta % 6 :
                color.G >= color.B ? (blue - red) / delta + 2 : (red - green) / delta + 4;
            hue = (hue * 60 + 360) % 360;
            if (format == SampleColorFormat.Hsv)
                return string.Create(CultureInfo.InvariantCulture,
                    $"hsv({hue:0}, {(max == 0 ? 0 : delta / max * 100):0}%, {max * 100:0}%)");
            return string.Create(CultureInfo.InvariantCulture,
                $"hsl({hue:0}, {saturation * 100:0}%, {light * 100:0}%)");
        }
    }
}

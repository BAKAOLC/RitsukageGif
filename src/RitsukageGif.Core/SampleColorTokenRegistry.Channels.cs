using System.Globalization;

namespace RitsukageGif.Core
{
    public sealed partial class SampleColorTokenRegistry
    {
        private void RegisterChannels()
        {
            RegisterByte("Re", values => values.Context.Red);
            RegisterByte("Gr", values => values.Context.Green);
            RegisterByte("Bl", values => values.Context.Blue);
            RegisterByte("Al", values => values.Context.Alpha);
            RegisterNumber("Cy", "Cmyk", 'p',
                values => values.Maximum == 0 ? 0 : (values.Maximum - values.Red) / values.Maximum);
            RegisterNumber("Ma", "Cmyk", 'p',
                values => values.Maximum == 0 ? 0 : (values.Maximum - values.Green) / values.Maximum);
            RegisterNumber("Ye", "Cmyk", 'p',
                values => values.Maximum == 0 ? 0 : (values.Maximum - values.Blue) / values.Maximum);
            RegisterNumber("Bk", "Cmyk", 'p', values => 1 - values.Maximum);
        }

        private void RegisterByte(string id, Func<SampleColorValues, byte> value)
        {
            Register(new(id, "Rgb", 'b', "bhHxXfF", (values, style) =>
            {
                var channel = value(values);
                return style switch
                {
                    'h' => (channel >> 4).ToString("x", CultureInfo.InvariantCulture),
                    'H' => (channel >> 4).ToString("X", CultureInfo.InvariantCulture),
                    'x' => channel.ToString("x2", CultureInfo.InvariantCulture),
                    'X' => channel.ToString("X2", CultureInfo.InvariantCulture),
                    'f' or 'F' => Number(channel / 255d, style),
                    _ => channel.ToString(CultureInfo.InvariantCulture),
                };
            }));
        }

        private void RegisterNumber(string id, string category, char defaultStyle,
            Func<SampleColorValues, double> value, SampleColorTokenInputs inputs = SampleColorTokenInputs.Color)
        {
            Register(new(id, category, defaultStyle, defaultStyle == 'p' ? "ipfF" : "ifF",
                (values, style) => Number(value(values), style), inputs));
        }

        private static string Number(double value, char style)
        {
            if (style is 'i' or 'p')
                return Math.Round(style == 'p' ? value * 100 : value, MidpointRounding.AwayFromZero)
                    .ToString(CultureInfo.InvariantCulture);
            var text = value.ToString("0.######", CultureInfo.InvariantCulture);
            if (text == "-0") return "0";
            return style == 'F' && text.StartsWith("0.", StringComparison.Ordinal) ? text[1..] : text;
        }
    }
}

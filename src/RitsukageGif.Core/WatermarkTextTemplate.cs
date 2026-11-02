using System.Globalization;
using System.Text;

namespace RitsukageGif.Core
{
    public sealed class WatermarkTextTemplate
    {
        private readonly (string Text, bool Date)[] _segments;

        private WatermarkTextTemplate((string Text, bool Date)[] segments)
        {
            _segments = segments;
        }

        public static bool TryCompile(string source, out WatermarkTextTemplate? result, out TemplateDiagnostic? error)
        {
            var segments = new List<(string Text, bool Date)>();
            var literal = new StringBuilder();
            for (var position = 0; position < source.Length; position++)
            {
                if (source[position] != '$')
                {
                    literal.Append(source[position]);
                    continue;
                }

                if (position + 1 < source.Length && source[position + 1] == '$')
                {
                    literal.Append('$');
                    position++;
                    continue;
                }

                if (literal.Length != 0)
                {
                    segments.Add((literal.ToString(), false));
                    literal.Clear();
                }

                var end = source.IndexOf('$', position + 1);
                if (end < 0)
                {
                    result = null;
                    error = new(position, 1, "Unclosed dollar sign in watermark time template.");
                    return false;
                }

                var format = source[(position + 1)..end];
                if (!ValidateQuotes(format))
                {
                    result = null;
                    error = new(position + 1, format.Length, "Unclosed quote in watermark time format.");
                    return false;
                }

                segments.Add((format, true));
                position = end;
            }

            if (literal.Length != 0) segments.Add((literal.ToString(), false));
            result = new([.. segments]);
            error = null;
            return true;
        }

        public string Format(DateTimeOffset timestamp)
        {
            var output = new StringBuilder();
            foreach (var segment in _segments)
                output.Append(segment.Date ? DateText(segment.Text, timestamp) : segment.Text);
            return output.ToString();
        }

        private static bool ValidateQuotes(string format)
        {
            var quoted = false;
            for (var index = 0; index < format.Length; index++)
            {
                if (format[index] != '\'') continue;
                if (index + 1 < format.Length && format[index + 1] == '\'')
                {
                    index++;
                    continue;
                }

                quoted = !quoted;
            }

            return !quoted;
        }

        private static string DateText(string format, DateTimeOffset time)
        {
            var output = new StringBuilder(format.Length + 16);
            var twelveHours = HasAmPm(format);
            for (var index = 0; index < format.Length;)
            {
                var character = format[index];
                // ReSharper disable once ConvertIfStatementToSwitchStatement
                if (character == '\'')
                {
                    if (index + 1 < format.Length && format[index + 1] == '\'')
                    {
                        output.Append('\'');
                        index += 2;
                        continue;
                    }

                    index++;
                    while (index < format.Length)
                    {
                        if (format[index] != '\'')
                        {
                            output.Append(format[index++]);
                            continue;
                        }

                        if (index + 1 < format.Length && format[index + 1] == '\'')
                        {
                            output.Append('\'');
                            index += 2;
                            continue;
                        }

                        index++;
                        break;
                    }

                    continue;
                }

                if (character is 'A' or 'a')
                {
                    var designator = time.Hour < 12 ? "AM" : "PM";
                    output.Append(character == 'a' ? designator.ToLowerInvariant() : designator);
                    index += index + 1 < format.Length && format[index + 1] == (character == 'a' ? 'p' : 'P') ? 2 : 1;
                    continue;
                }

                var count = 1;
                while (index + count < format.Length && format[index + count] == character) count++;
                var used = character switch
                {
                    'y' => count >= 4 ? 4 : count >= 2 ? 2 : 1, 'd' or 'M' or 't' => Math.Min(4, count),
                    'z' => count >= 3 ? 3 : 1, _ => Math.Min(2, count),
                };
                var token = new string(character, used);
                var text = character switch
                {
                    'y' when used >= 4 => time.Year.ToString("D4", CultureInfo.InvariantCulture),
                    'y' when used >= 2 => (time.Year % 100).ToString("D2", CultureInfo.InvariantCulture),
                    'M' when used >= 3 => time.ToString(used == 3 ? "MMM" : "MMMM", CultureInfo.InvariantCulture),
                    'M' => Digits(time.Month, used),
                    'd' when used >= 3 => time.ToString(used == 3 ? "ddd" : "dddd", CultureInfo.InvariantCulture),
                    'd' => Digits(time.Day, used),
                    'h' => Digits(twelveHours ? (time.Hour + 11) % 12 + 1 : time.Hour, used),
                    'H' => Digits(time.Hour, used),
                    'm' => Digits(time.Minute, used),
                    's' => Digits(time.Second, used),
                    'z' => used == 3 ? time.Millisecond.ToString("D3", CultureInfo.InvariantCulture) :
                        time.Millisecond.ToString("D3", CultureInfo.InvariantCulture).TrimEnd('0') is
                            { Length: > 0 } milliseconds ? milliseconds : "0",
                    't' => ZoneText(time, used),
                    _ => token,
                };
                output.Append(text);
                index += used;
            }

            return output.ToString();
        }

        private static bool HasAmPm(string format)
        {
            var quoted = false;
            for (var index = 0; index < format.Length; index++)
                if (format[index] == '\'')
                {
                    if (index + 1 < format.Length && format[index + 1] == '\'')
                    {
                        index++;
                        continue;
                    }

                    quoted = !quoted;
                }
                else if (!quoted && format[index] is 'A' or 'a')
                {
                    return true;
                }

            return false;
        }

        private static string Digits(int value, int count)
        {
            return value.ToString(count == 2 ? "D2" : "D", CultureInfo.InvariantCulture);
        }

        private static string ZoneText(DateTimeOffset time, int count)
        {
            var offset = time.ToString("zzz", CultureInfo.InvariantCulture);
            switch (count)
            {
                case 2: return offset.Replace(":", "", StringComparison.Ordinal);
                case 3: return offset;
            }

            var zone = TimeZoneInfo.Local;
            if (zone.GetUtcOffset(time) == time.Offset)
            {
                var name = zone.IsDaylightSavingTime(time) ? zone.DaylightName : zone.StandardName;
                return name;
            }

            return time.Offset == TimeSpan.Zero ? "UTC" : "UTC" + offset;
        }
    }
}

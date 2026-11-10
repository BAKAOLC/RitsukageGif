using System.Globalization;
using System.Text;

namespace RitsukageGif.Desktop
{
    internal static partial class ScreenshotPathTemplate
    {
        private static void AppendDate(StringBuilder result, ReadOnlySpan<char> format, DateTime timestamp)
        {
            var amPm = HasAmPm(format);
            var quoted = false;
            for (var index = 0; index < format.Length;)
            {
                var character = format[index];
                if (character == '\'')
                {
                    if (index + 1 < format.Length && format[index + 1] == '\'')
                    {
                        result.Append('\'');
                        index += 2;
                    }
                    else
                    {
                        quoted = !quoted;
                        index++;
                    }

                    continue;
                }

                if (quoted)
                {
                    result.Append(character);
                    index++;
                    continue;
                }

                var run = 1;
                while (index + run < format.Length && format[index + run] == character) run++;
                var count = character switch
                {
                    'd' or 'M' => Math.Min(run, 4),
                    'y' => run >= 4 ? 4 : run >= 2 ? 2 : 1,
                    'h' or 'H' or 'm' or 's' => Math.Min(run, 2),
                    'z' => run >= 3 ? 3 : 1,
                    _ => 1,
                };
                var culture = CultureInfo.CurrentCulture;
                var text = character switch
                {
                    'd' when count >= 3 => timestamp.ToString(count == 3 ? "ddd" : "dddd", culture),
                    'd' => timestamp.Day.ToString(count == 2 ? "D2" : "D", CultureInfo.InvariantCulture),
                    'M' when count >= 3 => timestamp.ToString(count == 3 ? "MMM" : "MMMM", culture),
                    'M' => timestamp.Month.ToString(count == 2 ? "D2" : "D", CultureInfo.InvariantCulture),
                    'y' when count == 4 => timestamp.Year.ToString("D4", CultureInfo.InvariantCulture),
                    'y' when count == 2 => (timestamp.Year % 100).ToString("D2", CultureInfo.InvariantCulture),
                    'h' or 'H' => (character == 'h' && amPm ? (timestamp.Hour + 11) % 12 + 1 : timestamp.Hour)
                        .ToString(count == 2 ? "D2" : "D", CultureInfo.InvariantCulture),
                    'm' => timestamp.Minute.ToString(count == 2 ? "D2" : "D", CultureInfo.InvariantCulture),
                    's' => timestamp.Second.ToString(count == 2 ? "D2" : "D", CultureInfo.InvariantCulture),
                    'z' when count == 3 => timestamp.Millisecond.ToString("D3", CultureInfo.InvariantCulture),
                    'z' => Milliseconds(timestamp.Millisecond),
                    'A' => (timestamp.Hour < 12
                            ? culture.DateTimeFormat.AMDesignator
                            : culture.DateTimeFormat.PMDesignator)
                        .ToUpperInvariant(),
                    'a' => (timestamp.Hour < 12
                            ? culture.DateTimeFormat.AMDesignator
                            : culture.DateTimeFormat.PMDesignator)
                        .ToLowerInvariant(),
                    't' => timestamp.Kind == DateTimeKind.Utc ? "UTC" :
                        TimeZoneInfo.Local.IsDaylightSavingTime(timestamp) ? TimeZoneInfo.Local.DaylightName :
                        TimeZoneInfo.Local.StandardName,
                    _ => character.ToString(),
                };
                if (character is 'A' or 'a' && index + 1 < format.Length &&
                    format[index + 1] == (character == 'A' ? 'P' : 'p')) count = 2;
                result.Append(text);
                index += count;
            }
        }

        private static string Milliseconds(int value)
        {
            return value == 0 ? "0" : value.ToString("D3", CultureInfo.InvariantCulture).TrimEnd('0');
        }

        private static bool HasAmPm(ReadOnlySpan<char> format)
        {
            var quoted = false;
            for (var index = 0; index < format.Length; index++)
                if (format[index] == '\'')
                {
                    if (index + 1 < format.Length && format[index + 1] == '\'') index++;
                    else quoted = !quoted;
                }
                else if (!quoted && format[index] is 'a' or 'A')
                {
                    return true;
                }

            return false;
        }
    }
}

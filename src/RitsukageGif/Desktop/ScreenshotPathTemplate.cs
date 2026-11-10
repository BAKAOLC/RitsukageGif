using System.Globalization;
using System.Text;

namespace RitsukageGif.Desktop
{
    internal static partial class ScreenshotPathTemplate
    {
        internal static string Expand(string template, ScreenshotNameContext context)
        {
            if (template.AsSpan().IndexOfAny('$', '%') < 0) return Sanitize(template);
            var result = new StringBuilder(template.Length + 32);
            var start = 0;
            while (start < template.Length)
            {
                var offset = template.AsSpan(start).IndexOfAny('$', '%');
                if (offset < 0)
                {
                    result.Append(template.AsSpan(start));
                    break;
                }

                var open = start + offset;
                result.Append(template.AsSpan(start, offset));
                var delimiter = template[open];
                var close = template.IndexOf(delimiter, open + 1);
                if (close < 0)
                    throw new ArgumentException(UiText.Format("ScreenshotTemplateUnclosed", delimiter),
                        nameof(template));
                var expression = template.AsSpan(open + 1, close - open - 1);
                if (expression.IsEmpty) result.Append(delimiter);
                else if (delimiter == '$') AppendDate(result, expression, context.Timestamp);
                else result.Append(Variable(expression, context));
                start = close + 1;
            }

            return Sanitize(result.ToString());
        }

        private static string Sanitize(string path)
        {
            var first = path.AsSpan().IndexOfAny("*?\"<>|".AsSpan());
            if (first < 0) return path;
            var result = new StringBuilder(path.Length);
            result.Append(path.AsSpan(0, first));
            foreach (var character in path.AsSpan(first))
                if (character is not ('*' or '?' or '"' or '<' or '>' or '|'))
                    result.Append(character);
            return result.ToString();
        }

        private static string Variable(ReadOnlySpan<char> expression, ScreenshotNameContext context)
        {
            var limit = -1;
            var hash = expression.LastIndexOf('#');
            if (hash >= 0)
            {
                if (!int.TryParse(expression[(hash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out limit))
                    throw new ArgumentException(UiText.Get("ScreenshotTemplateLength"), nameof(expression));
                expression = expression[..hash];
            }

            var removeExtension = expression.EndsWith("!", StringComparison.Ordinal);
            if (removeExtension) expression = expression[..^1];
            var raw = expression.EndsWith("&", StringComparison.Ordinal);
            if (raw) expression = expression[..^1];
            var value = ResolveVariable(expression, context, out var directory);
            if (removeExtension)
            {
                var dot = value.LastIndexOf('.');
                if (dot >= 0) value = value[..dot];
            }

            if (limit >= 0 && value.Length > limit)
            {
                if (limit > 0 && char.IsHighSurrogate(value[limit - 1])) limit--;
                value = value[..limit];
            }

            var result = new StringBuilder(value.Length);
            foreach (var character in value)
                result.Append(character is '*' or '?' or '"' or '<' or '>' or '|' ||
                              (!raw && !directory && character is '\\' or '/' or ':')
                    ? '-'
                    : character);
            return result.ToString();
        }

        private static string ResolveVariable(ReadOnlySpan<char> name, ScreenshotNameContext context,
            out bool directory)
        {
            directory = true;
            if (name.Equals("MYDOCUMENTS", StringComparison.OrdinalIgnoreCase))
                return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (name.Equals("MYDESKTOP", StringComparison.OrdinalIgnoreCase))
                return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (name.Equals("ONEDRIVE", StringComparison.OrdinalIgnoreCase))
                return Environment.GetEnvironmentVariable("OneDrive") ??
                       Environment.GetEnvironmentVariable("OneDriveConsumer") ??
                       Environment.GetEnvironmentVariable("OneDriveCommercial") ??
                       Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive");
            directory = false;
            if (name.Equals("COMPUTERNAME", StringComparison.OrdinalIgnoreCase)) return Environment.MachineName;
            if (name.Equals("USERNAME", StringComparison.OrdinalIgnoreCase)) return Environment.UserName;
            return name switch
            {
                "title" => Nonempty(context.Title, context.Process),
                "process" => Nonempty(context.Process, context.Title),
                "width" => context.Width.ToString(CultureInfo.InvariantCulture),
                "height" => context.Height.ToString(CultureInfo.InvariantCulture),
                "uuid" => context.Id.ToString("B"),
                "uuidv7" => context.TimeOrderedId.ToString("D"),
                "os" => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux",
                "timestampS" => new DateTimeOffset(context.Timestamp).ToUnixTimeSeconds()
                    .ToString(CultureInfo.InvariantCulture),
                "timestampMS" => new DateTimeOffset(context.Timestamp).ToUnixTimeMilliseconds()
                    .ToString(CultureInfo.InvariantCulture),
                _ => Environment.GetEnvironmentVariable(name.ToString()) ??
                     throw new ArgumentException(UiText.Format("ScreenshotTemplateVariable", name.ToString()),
                         nameof(name)),
            };
        }

        private static string Nonempty(string? first, string? second)
        {
            return !string.IsNullOrEmpty(first) ? first : !string.IsNullOrEmpty(second) ? second : "unknown";
        }
    }
}

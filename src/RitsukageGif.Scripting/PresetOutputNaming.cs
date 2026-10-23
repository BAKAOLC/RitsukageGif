using System.Globalization;
using System.Text;

namespace RitsukageGif.Scripting
{
    public static class PresetOutputNaming
    {
        private static readonly string[] Tokens =
            ["preset", "title", "process", "date", "time", "timestamp", "id", "sequence", "ext"];

        public static void Validate(string directory, string filename)
        {
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(filename) ||
                filename.IndexOfAny(['/', '\\', ':']) >= 0)
                throw new FormatException(UiText.Get("DslOutputInvalid"));
            Expand(filename, new Dictionary<string, string>());
        }

        public static string Filename(string template, string preset, string title, string process, string extension,
            int sequence, DateTime timestamp)
        {
            var values = new Dictionary<string, string>
            {
                ["preset"] = preset, ["title"] = title, ["process"] = process, ["ext"] = extension.TrimStart('.'),
                ["date"] = timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["time"] = timestamp.ToString("HH-mm-ss", CultureInfo.InvariantCulture),
                ["timestamp"] = timestamp.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture),
                ["id"] = Guid.NewGuid().ToString("N"),
                ["sequence"] = sequence.ToString("D3", CultureInfo.InvariantCulture)
            };
            var name = Expand(template, values);
            var invalid = Path.GetInvalidFileNameChars();
            name = new string(name.Select(c => invalid.Contains(c) || c < ' ' ? '_' : c).ToArray()).Trim().TrimEnd('.');
            if (name.Length > 180) name = name[..180];
            if (name.Length == 0 || name is "." or "..") name = "recording";
            var stem = name.Split('.')[0];
            if (new[]
                {
                    "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                    "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
                }.Contains(stem, StringComparer.OrdinalIgnoreCase)) name = "_" + name;
            if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) name += extension;
            return name;
        }

        private static string Expand(string template, IReadOnlyDictionary<string, string> values)
        {
            var result = new StringBuilder();
            for (var i = 0; i < template.Length; i++)
            {
                if (template[i] == '}') throw new FormatException(UiText.Get("DslOutputInvalid"));
                if (template[i] != '{')
                {
                    result.Append(template[i]);
                    continue;
                }

                var end = template.IndexOf('}', i + 1);
                if (end < 0) throw new FormatException(UiText.Get("DslOutputInvalid"));
                var token = template[(i + 1)..end];
                if (!Tokens.Contains(token)) throw new FormatException(UiText.Format("DslUnknownName", token));
                result.Append(values.TryGetValue(token, out var value) ? value : token);
                i = end;
            }

            return result.ToString();
        }
    }
}

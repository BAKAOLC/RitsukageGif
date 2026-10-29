using System.Text;

namespace RitsukageGif.Scripting
{
    public static partial class DataScriptLibrary
    {
        private static List<object?> ParseCsv(string text, char delimiter)
        {
            CheckSize(Utf8.GetByteCount(text));
            var rows = new List<object?>();
            if (text.Length == 0) return rows;
            var row = new List<object?>();
            var field = new StringBuilder();
            var quoted = false;
            var closed = false;
            var started = false;
            var fields = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var character = text[i];
                if (quoted)
                {
                    if (character != '"')
                    {
                        field.Append(character);
                    }
                    else if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                        closed = true;
                    }

                    continue;
                }

                if (character == delimiter || character is '\r' or '\n')
                {
                    if (++fields > MaximumItems) throw new ArgumentException(UiText.Get("ServiceLimitExceeded"));
                    row.Add(field.ToString());
                    field.Clear();
                    closed = false;
                    started = false;
                    if (character == delimiter) continue;
                    rows.Add(row);
                    row = [];
                    if (character == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    continue;
                }

                if (closed || (character == '"' && started)) throw new FormatException(UiText.Get("ScriptTypeError"));
                if (character == '"') quoted = true;
                else field.Append(character);
                started = true;
            }

            if (quoted) throw new FormatException(UiText.Get("ScriptTypeError"));
            if (started || closed || field.Length > 0 || row.Count > 0 || text[^1] == delimiter)
            {
                if (++fields > MaximumItems) throw new ArgumentException(UiText.Get("ServiceLimitExceeded"));
                row.Add(field.ToString());
                rows.Add(row);
            }

            return rows;
        }

        private static string StringifyCsv(List<object?> rows, char delimiter)
        {
            var result = new StringBuilder();
            long size = 0;
            foreach (var value in rows)
            {
                if (value is not List<object?> row || row.Count == 0)
                    throw new ArgumentException(UiText.Get("ScriptTypeError"));
                for (var i = 0; i < row.Count; i++)
                {
                    if (row[i] is not string field) throw new ArgumentException(UiText.Get("ScriptTypeError"));
                    size += Utf8.GetByteCount(field) + 3L + field.Count(character => character == '"');
                    CheckSize(size);
                    if (i > 0) result.Append(delimiter);
                    if (field.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0)
                        result.Append('"').Append(field.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
                    else result.Append(field);
                }

                result.Append("\r\n");
                size += 2;
                CheckSize(size);
            }

            return result.ToString();
        }
    }
}

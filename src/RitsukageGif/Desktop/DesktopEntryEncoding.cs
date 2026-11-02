using System.Text;

namespace RitsukageGif.Desktop
{
    internal static class DesktopEntryEncoding
    {
        private static string ExecArgument(string path)
        {
            var argument = new StringBuilder("\"");
            foreach (var character in path)
                argument.Append(character switch
                {
                    '\\' => @"\\\\",
                    '"' => "\\\\\"",
                    '$' => @"\\$",
                    '`' => @"\\`",
                    '%' => "%%",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    _ => character.ToString(),
                });
            return argument.Append('"').ToString();
        }

        internal static string ExecCommand(string executable)
        {
            var path = Path.GetFullPath(executable);
            return path.Contains('=')
                ? "/bin/sh -c " + ExecArgument("exec \"$@\"") + " RitsukageGif " + ExecArgument(path)
                : ExecArgument(path);
        }

        internal static Dictionary<string, string> Fields(string entry)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            var desktopEntry = false;
            foreach (var line in entry.Split('\n'))
            {
                var text = line.Trim();
                if (text.StartsWith('['))
                {
                    desktopEntry = text == "[Desktop Entry]";
                    continue;
                }

                if (!desktopEntry || text.StartsWith('#')) continue;
                var separator = text.IndexOf('=');
                if (separator > 0) fields[text[..separator]] = text[(separator + 1)..];
            }

            return fields;
        }

        internal static bool MatchesExecutable(string entry, string executable)
        {
            var fields = Fields(entry);
            if (fields.GetValueOrDefault("Type") != "Application" ||
                string.Equals(fields.GetValueOrDefault("Hidden"), "true", StringComparison.OrdinalIgnoreCase) ||
                fields.GetValueOrDefault("Exec") is not { } command) return false;
            var quoted = ExecCommand(executable);
            if (command == quoted || command.StartsWith(quoted + " ", StringComparison.Ordinal)) return true;
            var path = Path.GetFullPath(executable);
            return !path.Any(character => char.IsWhiteSpace(character) || "=\"'\\$`%<>~|&;*?#()".Contains(character)) &&
                   (command == path || command.StartsWith(path + " ", StringComparison.Ordinal));
        }
    }
}

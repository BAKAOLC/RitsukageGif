namespace RitsukageGif.Desktop
{
    public sealed record HotkeyPolicyPreferences
    {
        public bool DisableInFullscreen { get; set; }
        public List<string> IgnoredPrograms { get; set; } = [];

        public HotkeyPolicyPreferences Normalize()
        {
            var programs = new List<string>();
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            foreach (var value in IgnoredPrograms ?? [])
            {
                if (!TryNormalizeProgram(value, out var item)) continue;
                if (!programs.Contains(item, StringComparer.OrdinalIgnoreCase)) programs.Add(item);
            }

            return this with { IgnoredPrograms = programs };
        }

        public static bool TryNormalizeProgram(string? value, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var item = value.Trim().Trim('"');
            if (item.Length is 0 or > 32767 || item.Any(char.IsControl) ||
                item.IndexOfAny(['*', '?', '<', '>', '|', '"']) >= 0)
                return false;
            if (item.Contains('/') || item.Contains('\\'))
            {
                if (!Path.IsPathFullyQualified(item)) return false;
                try
                {
                    item = Path.GetFullPath(item);
                }
                catch (Exception error) when (error is ArgumentException or NotSupportedException
                                                  or PathTooLongException)
                {
                    return false;
                }

                if (string.IsNullOrEmpty(Path.GetFileName(item))) return false;
            }
            else if (item.Contains(':') || item is "." or "..")
            {
                return false;
            }

            normalized = item;
            return true;
        }

        internal bool Ignores(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var file = Path.GetFileName(path);
            return IgnoredPrograms.Any(program => program.Contains('/') || program.Contains('\\')
                ? string.Equals(program, path, StringComparison.OrdinalIgnoreCase)
                : string.Equals(program, file, StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(program, Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase));
        }
    }
}

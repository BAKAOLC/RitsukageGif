using System.Text;

namespace RitsukageGif.Contracts
{
    public static class FormulaLatexFormatter
    {
        private static readonly (string Prefix, string Suffix)[] StripDelimiters =
        [
            ("$$", "$$"), ("\\[", "\\]"), ("\\(", "\\)"), ("$", "$"),
            ("\\begin{equation}", "\\end{equation}"), ("\\begin{equation*}", "\\end{equation*}"),
        ];

        public static string Format(string source, FormulaLatexFormat format)
        {
            source = Unwrap(source);
            var (prefix, suffix) = Delimiters(format);
            return prefix.Length == 0 ? source : string.Concat(prefix, source, suffix);
        }

        public static string FormatMany(IReadOnlyList<string> sources, FormulaLatexFormat format)
        {
            if (sources.Count == 1) return Format(sources[0], format);
            var (prefix, suffix) = Delimiters(format);
            var output = new StringBuilder();
            for (var index = 0; index < sources.Count; index++)
            {
                if (index > 0) output.Append('\n');
                output.Append(prefix).Append(Unwrap(sources[index])).Append(suffix);
            }

            return output.ToString();
        }

        public static string Unwrap(string displayed)
        {
            var value = displayed.AsSpan().Trim();
            foreach (var (prefix, suffix) in StripDelimiters)
                if (value.Length >= prefix.Length + suffix.Length &&
                    value.StartsWith(prefix, StringComparison.Ordinal) &&
                    value.EndsWith(suffix, StringComparison.Ordinal))
                    return value.Slice(prefix.Length, value.Length - prefix.Length - suffix.Length).Trim().ToString();
            return displayed;
        }

        private static (string Prefix, string Suffix) Delimiters(FormulaLatexFormat format)
        {
            return format switch
            {
                FormulaLatexFormat.FormulaOnly => ("", ""),
                FormulaLatexFormat.InlineDollar => ("$", "$"),
                FormulaLatexFormat.DisplayDollar => ("$$\n", "\n$$"),
                FormulaLatexFormat.DisplayBrackets => ("\\[\n", "\n\\]"),
                FormulaLatexFormat.InlineBrackets => ("\\(", "\\)"),
                FormulaLatexFormat.Equation => ("\\begin{equation}\n", "\n\\end{equation}"),
                FormulaLatexFormat.EquationStar => ("\\begin{equation*}\n", "\n\\end{equation*}"),
                _ => throw new ArgumentOutOfRangeException(nameof(format)),
            };
        }
    }
}

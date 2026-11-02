using System.Globalization;
using System.Text;
using CSharpMath.Atom;

namespace RitsukageGif.Core
{
    public static partial class FormulaTextExporter
    {
        private static bool TryExport(string latex, FormulaTextFormat format, out string? text, out string? error)
        {
            text = null;
            error = null;
            if (!Enum.IsDefined(format)) return Fail(out error, "The formula text format is not supported.");
            if (string.IsNullOrWhiteSpace(latex)) return Fail(out error, "The formula is empty.");
            if (latex.Length > 65536) return Fail(out error, "The formula is too long to convert.");
            var nesting = 0;
            for (var index = 0; index < latex.Length; index++)
                switch (latex[index])
                {
                    case '\\':
                        index++;
                        break;
                    case '{' when ++nesting > 128:
                        return Fail(out error, "The formula nesting is too deep to convert.");
                    case '}':
                        nesting = Math.Max(0, nesting - 1);
                        break;
                }

            new LaTeXParser(latex).Build().Deconstruct(out var list, out var syntaxError);
            if (syntaxError is not null) return Fail(out error, "The formula syntax is invalid.");
            if (list.Count == 0) return Fail(out error, "The formula has no content.");
            if (format == FormulaTextFormat.Latex)
            {
                text = LaTeXParser.MathListToLaTeX(list).ToString();
                return true;
            }

            var builder = new StringBuilder();
            if (format == FormulaTextFormat.MathMl)
            {
                if (!WriteMathMl(list, builder, out error)) return false;
            }
            else if (!WriteLinear(list, builder, format, 0, out error))
            {
                return false;
            }

            text = builder.ToString();
            return true;
        }

        private static bool Fail(out string? error, string message)
        {
            error = message;
            return false;
        }

        private static string Number(float value)
        {
            return value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        private static void Quote(StringBuilder builder, string text)
        {
            builder.Append('"');
            foreach (var character in text)
                switch (character)
                {
                    case '\\': builder.Append(@"\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default: builder.Append(character); break;
                }

            builder.Append('"');
        }
    }
}

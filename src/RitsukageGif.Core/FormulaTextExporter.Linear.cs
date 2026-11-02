using System.Text;
using CSharpMath.Atom;
using A = CSharpMath.Atom.Atoms;

namespace RitsukageGif.Core
{
    public static partial class FormulaTextExporter
    {
        private static bool WriteLinear(MathList list, StringBuilder builder, FormulaTextFormat format, int depth,
            out string? error)
        {
            if (depth > 128) return Fail(out error, "The formula nesting is too deep to convert.");
            var first = true;
            foreach (var atom in list)
            {
                if (!first && format != FormulaTextFormat.Text) builder.Append(' ');
                first = false;
                if (!LinearAtom(atom, builder, format, depth + 1, out error)) return false;
            }

            error = null;
            return true;
        }

        private static bool LinearAtom(MathAtom atom, StringBuilder builder, FormulaTextFormat format, int depth,
            out string? error)
        {
            var style = format == FormulaTextFormat.Text ? null : LinearStyle(atom.FontStyle, format);
            if (atom.FontStyle != FontStyle.Default && format != FormulaTextFormat.Text && style is null)
                return Fail(out error, "This formula font style is not supported by the selected format.");
            if (!string.IsNullOrEmpty(style)) builder.Append(style).Append('(');
            if (!LinearBody(atom, builder, format, depth, out error)) return false;
            if (!string.IsNullOrEmpty(style)) builder.Append(')');
            if (atom.Subscript.Count != 0)
            {
                builder.Append("_(");
                if (!WriteLinear(atom.Subscript, builder, format, depth, out error)) return false;
                builder.Append(')');
            }

            if (atom.Superscript.Count != 0)
            {
                builder.Append("^(");
                if (!WriteLinear(atom.Superscript, builder, format, depth, out error)) return false;
                builder.Append(')');
            }

            return true;
        }

        private static bool LinearBody(MathAtom atom, StringBuilder builder, FormulaTextFormat format, int depth,
            out string? error)
        {
            error = null;
            switch (atom)
            {
                case A.Fraction fraction:
                    if (!fraction.HasRule)
                    {
                        if (format == FormulaTextFormat.Typst)
                        {
                            if (fraction.LeftDelimiter.Nucleus != "(" || fraction.RightDelimiter.Nucleus != ")")
                                return Fail(out error, "This stacked fraction is not supported by Typst export.");
                            builder.Append("binom(");
                        }
                        else
                        {
                            if (!Boundary(fraction.LeftDelimiter.Nucleus, true, builder, format, out error))
                                return false;
                            builder.Append(format == FormulaTextFormat.Text ? "stack(" : "stackrel(");
                        }
                    }
                    else
                    {
                        builder.Append(format == FormulaTextFormat.Typst ? "frac(" : "(");
                    }

                    if (!WriteLinear(fraction.Numerator, builder, format, depth, out error)) return false;
                    builder.Append(!fraction.HasRule && format == FormulaTextFormat.AsciiMath ? ")(" :
                        fraction.HasRule && format != FormulaTextFormat.Typst ? ")/(" : ", ");
                    if (!WriteLinear(fraction.Denominator, builder, format, depth, out error)) return false;
                    builder.Append(')');
                    if (!fraction.HasRule && format != FormulaTextFormat.Typst &&
                        !Boundary(fraction.RightDelimiter.Nucleus, false, builder, format, out error)) return false;
                    return true;
                case A.Radical radical:
                    builder.Append(radical.Degree.Count == 0 ? "sqrt(" : "root(");
                    if (radical.Degree.Count != 0)
                    {
                        if (!WriteLinear(radical.Degree, builder, format, depth, out error)) return false;
                        builder.Append(format == FormulaTextFormat.Typst ? ", " : ")(");
                    }

                    if (!WriteLinear(radical.Radicand, builder, format, depth, out error)) return false;
                    builder.Append(')');
                    return true;
                case A.Inner inner:
                    // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                    switch (format)
                    {
                        case FormulaTextFormat.AsciiMath when inner.InnerList is
                            [A.Table { Superscript.Count: 0, Subscript.Count: 0 } innerTable]:
                            return LinearTable(innerTable, builder, format, depth, inner.LeftBoundary.Nucleus,
                                inner.RightBoundary.Nucleus, out error);
                        case FormulaTextFormat.Typst:
                            builder.Append("lr(");
                            break;
                    }

                    if (!Boundary(inner.LeftBoundary.Nucleus, true, builder, format, out error)) return false;
                    if (!WriteLinear(inner.InnerList, builder, format, depth, out error)) return false;
                    if (!Boundary(inner.RightBoundary.Nucleus, false, builder, format, out error)) return false;
                    if (format == FormulaTextFormat.Typst) builder.Append(')');
                    return true;
                case A.Table table:
                    return LinearTable(table, builder, format, depth, null, null, out error);
                case A.Accent accent:
                    var decoration = AccentName(accent.Nucleus, format);
                    if (decoration is null)
                        return Fail(out error, "This formula accent is not supported by the selected format.");
                    return LinearDecoration(accent.InnerList, decoration, builder, format, depth, out error);
                case A.Overline overline:
                    return LinearDecoration(overline.InnerList,
                        format == FormulaTextFormat.AsciiMath ? "bar" : "overline", builder, format, depth, out error);
                case A.Underline underline:
                    return LinearDecoration(underline.InnerList,
                        format == FormulaTextFormat.AsciiMath ? "ul" : "underline", builder, format, depth, out error);
                case A.Colored colored:
                    if (format != FormulaTextFormat.Text)
                        return Fail(out error, "Formula colors cannot be preserved in the selected text format.");
                    return WriteLinear(colored.InnerList, builder, format, depth, out error);
                case A.ColorBox box:
                    if (format != FormulaTextFormat.Text)
                        return Fail(out error,
                            "Formula background colors cannot be preserved in the selected text format.");
                    return WriteLinear(box.InnerList, builder, format, depth, out error);
                case A.Space:
                    builder.Append(' ');
                    return true;
                case A.Comment or A.Style:
                    return true;
                case A.Prime prime:
                    builder.Append('\'', prime.Length);
                    return true;
                case A.Number:
                    builder.Append(atom.Nucleus);
                    return true;
                case A.LargeOperator large:
                    // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                    switch (format)
                    {
                        case FormulaTextFormat.Typst:
                            builder.Append("op(");
                            Quote(builder, large.Nucleus);
                            if (large.Limits is { } limits)
                                builder.Append(limits ? ", limits: #true" : ", limits: #false");
                            builder.Append(')');
                            return true;
                        case FormulaTextFormat.AsciiMath when atom.Nucleus.Length > 1 &&
                                                              atom.Nucleus.All(char.IsLetter) &&
                                                              atom.Nucleus is not ("sin" or "cos" or "tan" or "cot" or
                                                                  "sec" or "csc" or "sinh" or "cosh" or "tanh"
                                                                  or "arcsin" or
                                                                  "arccos" or "arctan" or "exp" or "log" or "ln"
                                                                  or "lim" or
                                                                  "min" or "max" or "det" or "gcd"):
                            return Fail(out error, "This formula operator is not supported by AsciiMath export.");
                    }

                    return Symbol(atom.Nucleus, builder, format, out error);
                case A.Open:
                    return Boundary(atom.Nucleus, true, builder, format, out error);
                case A.Close:
                    return Boundary(atom.Nucleus, false, builder, format, out error);
                case A.Variable or A.Ordinary:
                    if (format != FormulaTextFormat.Text && atom.FontStyle == FontStyle.Roman)
                    {
                        if (format == FormulaTextFormat.AsciiMath && atom.Nucleus.IndexOfAny(['"', '\\']) >= 0)
                            return Fail(out error, "This formula text cannot be quoted as AsciiMath.");
                        Quote(builder, atom.Nucleus);
                        return true;
                    }

                    return Symbol(atom.Nucleus, builder, format, out error);
                case A.BinaryOperator or A.UnaryOperator or A.Relation or A.Punctuation:
                    return Symbol(atom.Nucleus, builder, format, out error);
                default:
                    return Fail(out error, "This formula construct cannot be represented by the selected format.");
            }
        }

        private static bool LinearTable(A.Table table, StringBuilder builder, FormulaTextFormat format, int depth,
            string? left, string? right, out string? error)
        {
            if (format == FormulaTextFormat.AsciiMath && table.Cells.Any(row => row.Count != table.NColumns))
                return Fail(out error, "This non-rectangular matrix cannot be represented as AsciiMath.");
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (format)
            {
                case FormulaTextFormat.Typst:
                    builder.Append("mat(delim: #none, ");
                    break;
                case FormulaTextFormat.Text:
                    builder.Append('[');
                    break;
                default:
                    if (!Boundary(left, true, builder, format, out error)) return false;
                    break;
            }

            for (var row = 0; row < table.Cells.Count; row++)
            {
                if (row != 0)
                    builder.Append(format switch
                    {
                        FormulaTextFormat.Typst or FormulaTextFormat.Text => "; ", _ => ",",
                    });
                if (format != FormulaTextFormat.Typst) builder.Append('(');
                for (var column = 0; column < table.Cells[row].Count; column++)
                {
                    if (column != 0) builder.Append(", ");
                    if (!WriteLinear(table.Cells[row][column], builder, format, depth, out error)) return false;
                }

                if (format != FormulaTextFormat.Typst) builder.Append(')');
            }

            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (format)
            {
                case FormulaTextFormat.Typst:
                    builder.Append(')');
                    break;
                case FormulaTextFormat.Text:
                    builder.Append(']');
                    break;
                default:
                    if (!Boundary(right, false, builder, format, out error)) return false;
                    break;
            }

            error = null;
            return true;
        }

        private static bool LinearDecoration(MathList list, string name, StringBuilder builder,
            FormulaTextFormat format, int depth, out string? error)
        {
            builder.Append(name).Append('(');
            if (!WriteLinear(list, builder, format, depth, out error)) return false;
            builder.Append(')');
            return true;
        }

        private static string? LinearStyle(FontStyle style, FormulaTextFormat format)
        {
            return (style, format == FormulaTextFormat.Typst) switch
            {
                (FontStyle.Default, _) => null,
                (FontStyle.Roman, true) => "upright", (FontStyle.Roman, false) => "",
                (FontStyle.Bold, true) => "bold", (FontStyle.Bold, false) => "bb",
                (FontStyle.Italic, true) => "italic", (FontStyle.Italic, false) => "",
                (FontStyle.Caligraphic, true) => "cal", (FontStyle.Caligraphic, false) => "cc",
                (FontStyle.Fraktur, true) => "frak", (FontStyle.Fraktur, false) => "fr",
                (FontStyle.Blackboard, true) => "bb", (FontStyle.Blackboard, false) => "bbb",
                (FontStyle.SansSerif, true) => "sans", (FontStyle.SansSerif, false) => "sf",
                (FontStyle.Typewriter, false) => "tt", _ => null,
            };
        }

        private static string? AccentName(string accent, FormulaTextFormat format)
        {
            return accent switch
            {
                "̂" or "^" => "hat", "˜" or "̃" or "~" => "tilde",
                "̄" or "¯" => format == FormulaTextFormat.AsciiMath ? "bar" : "overline",
                "̇" or "˙" => "dot", "̈" or "¨" => "ddot",
                "⃗" or "→" => format == FormulaTextFormat.Typst ? "arrow" : "vec",
                _ => null,
            };
        }
    }
}

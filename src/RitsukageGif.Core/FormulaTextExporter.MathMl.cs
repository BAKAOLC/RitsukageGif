using System.Drawing;
using System.Text;
using System.Xml;
using CSharpMath.Atom;
using A = CSharpMath.Atom.Atoms;

namespace RitsukageGif.Core
{
    public static partial class FormulaTextExporter
    {
        private const string MathNamespace = "http://www.w3.org/1998/Math/MathML";

        private static bool WriteMathMl(MathList list, StringBuilder builder, out string? error)
        {
            using var writer = XmlWriter.Create(builder, new() { OmitXmlDeclaration = true, CheckCharacters = true });
            writer.WriteStartElement("math", MathNamespace);
            var success = MathRow(list, writer, 0, out error);
            writer.WriteEndElement();
            return success;
        }

        private static bool MathRow(MathList list, XmlWriter writer, int depth, out string? error)
        {
            if (depth > 128) return Fail(out error, "The formula nesting is too deep to convert.");
            writer.WriteStartElement("mrow", MathNamespace);
            foreach (var atom in list)
                if (!MathAtom(atom, writer, depth + 1, out error))
                    return false;
            writer.WriteEndElement();
            error = null;
            return true;
        }

        private static bool MathAtom(MathAtom atom, XmlWriter writer, int depth, out string? error)
        {
            var styled = atom.FontStyle != FontStyle.Default;
            if (styled)
            {
                writer.WriteStartElement("mstyle", MathNamespace);
                writer.WriteAttributeString("mathvariant", atom.FontStyle switch
                {
                    FontStyle.Roman => "normal", FontStyle.Bold => "bold", FontStyle.Italic => "italic",
                    FontStyle.BoldItalic => "bold-italic", FontStyle.Caligraphic => "script",
                    FontStyle.Fraktur => "fraktur", FontStyle.Blackboard => "double-struck",
                    FontStyle.Typewriter => "monospace", FontStyle.SansSerif => "sans-serif", _ => "normal",
                });
            }

            var sub = atom.Subscript.Count != 0;
            var sup = atom.Superscript.Count != 0;
            var limits = atom is A.LargeOperator { Limits: true };
            if (sub || sup)
                writer.WriteStartElement((sub, sup, limits) switch
                {
                    (true, true, true) => "munderover", (true, false, true) => "munder", (false, true, true) => "mover",
                    (true, true, false) => "msubsup", (true, false, false) => "msub", _ => "msup",
                }, MathNamespace);
            if (!MathBody(atom, writer, depth, out error)) return false;
            if (sub && !MathRow(atom.Subscript, writer, depth, out error)) return false;
            if (sup && !MathRow(atom.Superscript, writer, depth, out error)) return false;
            if (sub || sup) writer.WriteEndElement();
            if (styled) writer.WriteEndElement();
            return true;
        }

        private static bool MathBody(MathAtom atom, XmlWriter writer, int depth, out string? error)
        {
            error = null;
            switch (atom)
            {
                case A.Fraction fraction:
                    writer.WriteStartElement("mrow", MathNamespace);
                    MathBoundary(fraction.LeftDelimiter.Nucleus, writer);
                    writer.WriteStartElement("mfrac", MathNamespace);
                    if (!fraction.HasRule) writer.WriteAttributeString("linethickness", "0");
                    if (!MathRow(fraction.Numerator, writer, depth, out error) ||
                        !MathRow(fraction.Denominator, writer, depth, out error)) return false;
                    writer.WriteEndElement();
                    MathBoundary(fraction.RightDelimiter.Nucleus, writer);
                    writer.WriteEndElement();
                    return true;
                case A.Radical radical:
                    writer.WriteStartElement(radical.Degree.Count == 0 ? "msqrt" : "mroot", MathNamespace);
                    if (!MathRow(radical.Radicand, writer, depth, out error)) return false;
                    if (radical.Degree.Count != 0 && !MathRow(radical.Degree, writer, depth, out error)) return false;
                    writer.WriteEndElement();
                    return true;
                case A.Inner inner:
                    writer.WriteStartElement("mrow", MathNamespace);
                    MathBoundary(inner.LeftBoundary.Nucleus, writer);
                    if (!MathRow(inner.InnerList, writer, depth, out error)) return false;
                    MathBoundary(inner.RightBoundary.Nucleus, writer);
                    writer.WriteEndElement();
                    return true;
                case A.Table table:
                    writer.WriteStartElement("mtable", MathNamespace);
                    if (table.Alignments.Count != 0)
                        writer.WriteAttributeString("columnalign",
                            string.Join(" ",
                                table.Alignments.Select(alignment => alignment.ToString().ToLowerInvariant())));
                    foreach (var row in table.Cells)
                    {
                        writer.WriteStartElement("mtr", MathNamespace);
                        foreach (var cell in row)
                        {
                            writer.WriteStartElement("mtd", MathNamespace);
                            if (!MathRow(cell, writer, depth, out error)) return false;
                            writer.WriteEndElement();
                        }

                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                    return true;
                case A.Accent accent:
                    return MathDecoration(accent.InnerList, accent.Nucleus, false, writer, depth, out error);
                case A.Overline overline:
                    return MathDecoration(overline.InnerList, "¯", false, writer, depth, out error);
                case A.Underline underline:
                    return MathDecoration(underline.InnerList, "_", true, writer, depth, out error);
                case A.Colored colored:
                    writer.WriteStartElement("mstyle", MathNamespace);
                    writer.WriteAttributeString("mathcolor", ColorValue(colored.Color));
                    if (!MathRow(colored.InnerList, writer, depth, out error)) return false;
                    writer.WriteEndElement();
                    return true;
                case A.ColorBox box:
                    writer.WriteStartElement("mstyle", MathNamespace);
                    writer.WriteAttributeString("mathbackground", ColorValue(box.Color));
                    if (!MathRow(box.InnerList, writer, depth, out error)) return false;
                    writer.WriteEndElement();
                    return true;
                case A.Space space:
                    writer.WriteStartElement("mspace", MathNamespace);
                    writer.WriteAttributeString("width",
                        Number(space.IsMu ? space.Length / 18 : space.Length) + (space.IsMu ? "em" : "pt"));
                    writer.WriteEndElement();
                    return true;
                case A.Comment or A.Style:
                    writer.WriteStartElement("mrow", MathNamespace);
                    writer.WriteEndElement();
                    return true;
                case A.Prime prime:
                    MathToken("mo", new('\u2032', prime.Length), writer);
                    return true;
                case A.Number:
                    MathToken("mn", atom.Nucleus, writer);
                    return true;
                case A.Variable:
                    MathToken("mi", atom.Nucleus, writer);
                    return true;
                case A.Ordinary:
                    MathToken(atom.FontStyle == FontStyle.Roman ? "mtext" : "mi", atom.Nucleus, writer);
                    return true;
                case A.BinaryOperator or A.UnaryOperator or A.Relation or A.Open or A.Close or A.Punctuation
                    or A.LargeOperator:
                    MathToken("mo", atom.Nucleus, writer);
                    return true;
                default:
                    return Fail(out error, "This formula construct cannot be represented as MathML.");
            }
        }

        private static bool MathDecoration(MathList list, string mark, bool under, XmlWriter writer, int depth,
            out string? error)
        {
            writer.WriteStartElement(under ? "munder" : "mover", MathNamespace);
            writer.WriteAttributeString(under ? "accentunder" : "accent", "true");
            if (!MathRow(list, writer, depth, out error)) return false;
            MathToken("mo", mark, writer);
            writer.WriteEndElement();
            return true;
        }

        private static void MathBoundary(string? boundary, XmlWriter writer)
        {
            if (!string.IsNullOrEmpty(boundary)) MathToken("mo", boundary, writer);
        }

        private static void MathToken(string token, string value, XmlWriter writer)
        {
            writer.WriteStartElement(token, MathNamespace);
            writer.WriteString(value);
            writer.WriteEndElement();
        }

        private static string ColorValue(Color color)
        {
            return color.A == 255
                ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                : $"rgba({color.R},{color.G},{color.B},{Number(color.A / 255f)})";
        }
    }
}

using System.Globalization;
using System.Xml.Linq;

namespace RitsukageGif.Core
{
    internal static partial class MathMlToOmml
    {
        private static XElement Run(string text, EquationStyle style, bool variable)
        {
            var variant = style.Variant ?? (variable ? "italic" : "normal");
            var (script, emphasis) = variant switch
            {
                "normal" => ("roman", "p"), "bold" => ("roman", "b"),
                "italic" => ("roman", "i"), "bold-italic" => ("roman", "bi"),
                "double-struck" => ("double-struck", "p"), "script" => ("script", "p"),
                "bold-script" => ("script", "b"), "fraktur" => ("fraktur", "p"),
                "bold-fraktur" => ("fraktur", "b"), "sans-serif" => ("sans-serif", "p"),
                "bold-sans-serif" => ("sans-serif", "b"), "sans-serif-italic" => ("sans-serif", "i"),
                "sans-serif-bold-italic" => ("sans-serif", "bi"), "monospace" => ("monospace", "p"),
                _ => throw new NotSupportedException($"The MathML font variant '{variant}' is not supported."),
            };
            var properties = new XElement(Word + "rPr",
                new XElement(Word + "rFonts", new XAttribute(Word + "ascii", "Cambria Math"),
                    new XAttribute(Word + "hAnsi", "Cambria Math")));
            if (style.Color is not null)
                properties.Add(new XElement(Word + "color", new XAttribute(Word + "val", NormalizeColor(style.Color))));
            properties.Add(
                new XElement(Word + "sz", new XAttribute(Word + "val", "24")),
                new XElement(Word + "szCs", new XAttribute(Word + "val", "24")));

            if (style.Background is not null)
                properties.Add(new XElement(Word + "shd", new XAttribute(Word + "val", "clear"),
                    new XAttribute(Word + "color", "auto"),
                    new XAttribute(Word + "fill", NormalizeColor(style.Background))));
            return new(Math + "r", new XElement(Math + "rPr", Value("scr", script), Value("sty", emphasis)), properties,
                new XElement(Math + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text));
        }

        private static XElement Space(XElement element, EquationStyle style)
        {
            var width = (string?)element.Attribute("width") ?? "0em";
            var unit = width.EndsWith("em", StringComparison.Ordinal) ? 240d :
                width.EndsWith("pt", StringComparison.Ordinal) ? 20d :
                throw new NotSupportedException("Only em and pt MathML space units are supported.");
            if (!double.TryParse(width.AsSpan(0, width.Length - 2), CultureInfo.InvariantCulture, out var amount) ||
                !double.IsFinite(amount))
                throw new InvalidDataException("The MathML space width is invalid.");
            var spacing = System.Math.Round(amount * unit - 60);
            if (spacing is < -31680 or > 31680)
                throw new NotSupportedException("The MathML space width is outside the Word range.");
            var run = Run(" ", style with { Variant = "normal" }, false);
            run.Element(Word + "rPr")!.Element(Word + "sz")!.AddBeforeSelf(new XElement(Word + "spacing",
                new XAttribute(Word + "val", ((int)spacing).ToString(CultureInfo.InvariantCulture))));
            return run;
        }

        private static string NormalizeColor(string value)
        {
            if (value is ['#', _, _, _, _, _, _] && uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out _))
                return value[1..].ToUpperInvariant();
            if (value.StartsWith("rgba(", StringComparison.Ordinal) && value.EndsWith(')'))
            {
                var values = value[5..^1].Split(',');
                if (values.Length == 4 && byte.TryParse(values[0], CultureInfo.InvariantCulture, out var red) &&
                    byte.TryParse(values[1], CultureInfo.InvariantCulture, out var green) &&
                    byte.TryParse(values[2], CultureInfo.InvariantCulture, out var blue) &&
                    double.TryParse(values[3], CultureInfo.InvariantCulture, out var alpha) && double.IsFinite(alpha) &&
                    alpha is >= 0 and <= 1)
                    return
                        $"{(int)System.Math.Round(red * alpha + 255 * (1 - alpha)):X2}{(int)System.Math.Round(green * alpha + 255 * (1 - alpha)):X2}{(int)System.Math.Round(blue * alpha + 255 * (1 - alpha)):X2}";
            }

            throw new NotSupportedException("The MathML color cannot be represented in a Word document.");
        }

        private static XElement Matrix(XElement table, EquationStyle style, int depth, CancellationToken token)
        {
            var rows = table.Elements().ToArray();
            if (rows.Length == 0 || rows.Any(row =>
                    row.Name != Source + "mtr" || row.Elements().Any(cell => cell.Name != Source + "mtd")))
                throw new InvalidDataException("The MathML matrix contains invalid rows or cells.");
            var columns = rows.Max(row => row.Elements().Count());
            if (columns == 0) throw new InvalidDataException("The MathML matrix has no columns.");
            var alignments =
                ((string?)table.Attribute("columnalign") ?? "center").Split((char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries);
            var settings = new XElement(Math + "mcs");
            for (var column = 0; column < columns; column++)
            {
                var align = alignments.Length == 0
                    ? "center"
                    : alignments[System.Math.Min(column, alignments.Length - 1)];
                if (align is not ("left" or "center" or "right"))
                    throw new NotSupportedException("The MathML matrix alignment is not supported.");
                settings.Add(new XElement(Math + "mc",
                    new XElement(Math + "mcPr", Value("count", "1"), Value("mcJc", align))));
            }

            var matrix = new XElement(Math + "m", new XElement(Math + "mPr", settings));
            foreach (var row in rows)
            {
                var result = new XElement(Math + "mr");
                foreach (var cell in row.Elements())
                    result.Add(new XElement(Math + "e", Children(cell, style, depth + 1, token)));
                while (result.Elements().Count() < columns) result.Add(new XElement(Math + "e"));
                matrix.Add(result);
            }

            return matrix;
        }
    }
}

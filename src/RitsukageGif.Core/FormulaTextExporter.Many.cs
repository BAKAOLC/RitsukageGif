using System.Xml.Linq;

namespace RitsukageGif.Core
{
    public static partial class FormulaTextExporter
    {
        public static bool TryExportMany(IReadOnlyList<string> sources, FormulaTextFormat format,
            out string? text, out string? error)
        {
            text = null;
            error = null;
            switch (sources.Count)
            {
                case 0: return Fail(out error, "There are no formulas to convert.");
                case 1: return TryExport(sources[0], format, out text, out error);
            }

            var values = new List<string>(sources.Count);
            foreach (var source in sources)
            {
                if (!TryExport(source, format, out var value, out error)) return false;
                values.Add(value!);
            }

            if (format == FormulaTextFormat.MathMl)
            {
                XNamespace ns = MathNamespace;
                text = new XElement(ns + "math", new XElement(ns + "mtable", values.Select(value =>
                        new XElement(ns + "mtr", new XElement(ns + "mtd", XElement.Parse(value).Elements())))))
                    .ToString(SaveOptions.DisableFormatting);
            }
            else
            {
                text = format switch
                {
                    FormulaTextFormat.AsciiMath => "{:(" + string.Join("),(", values) + "):}",
                    FormulaTextFormat.Typst => string.Join(" \\ ", values),
                    _ => string.Join("\n\n", values),
                };
            }

            return true;
        }
    }
}

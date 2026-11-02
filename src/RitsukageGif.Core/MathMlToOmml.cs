using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace RitsukageGif.Core
{
    internal static partial class MathMlToOmml
    {
        private static readonly XNamespace Source = "http://www.w3.org/1998/Math/MathML";
        private static readonly XNamespace Math = "http://schemas.openxmlformats.org/officeDocument/2006/math";
        private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        internal static XElement Convert(string source, CancellationToken token)
        {
            using var reader = XmlReader.Create(new StringReader(source), new()
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024,
            });
            var document = XDocument.Load(reader);
            if (document.Root is not { } root || root.Name != Source + "math")
                throw new InvalidDataException("The formula is not a MathML math document.");
            return new(Math + "oMath", Children(root, Inherit(root, default), 0, token));
        }

        private static IEnumerable<XElement> Children(XElement element, EquationStyle style, int depth,
            CancellationToken token)
        {
            foreach (var child in element.Elements())
            foreach (var result in Map(child, style, depth + 1, token))
                yield return result;
        }

        private static IEnumerable<XElement> Map(XElement element, EquationStyle style, int depth,
            CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (depth > 128) throw new InvalidDataException("The MathML nesting is too deep.");
                if (element.Name.Namespace != Source)
                    throw new InvalidDataException("The MathML contains an unsupported namespace.");
                style = Inherit(element, style);
                var name = element.Name.LocalName;
                var parts = element.Elements().ToArray();
                switch (name)
                {
                    case "math" or "mrow" or "mstyle": return Children(element, style, depth, token);
                    case "mi" or "mo" or "mn" or "mtext":
                        return [Run(element.Value, style, name == "mi")];
                    case "mspace": return [Space(element, style)];
                    case "mfrac":
                        Require(parts, 2);
                        var fraction = new XElement(Math + "f");
                        if ((string?)element.Attribute("linethickness") == "0")
                            fraction.Add(new XElement(Math + "fPr", Value("type", "noBar")));
                        fraction.Add(Argument("num", parts[0], style, depth, token),
                            Argument("den", parts[1], style, depth, token));
                        return [fraction];
                    case "msqrt":
                        return
                        [
                            new(Math + "rad", new XElement(Math + "radPr", Value("degHide", "1")),
                                new XElement(Math + "deg"),
                                new XElement(Math + "e", Children(element, style, depth, token))),
                        ];
                    case "mroot":
                        Require(parts, 2);
                        return
                        [
                            new(Math + "rad", Argument("deg", parts[1], style, depth, token),
                                Argument("e", parts[0], style, depth, token)),
                        ];
                    case "msub" or "msup" or "msubsup":
                        Require(parts, name == "msubsup" ? 3 : 2);
                        var script = new XElement(
                            Math + name switch { "msub" => "sSub", "msup" => "sSup", _ => "sSubSup" },
                            Argument("e", parts[0], style, depth, token));
                        if (name != "msup") script.Add(Argument("sub", parts[1], style, depth, token));
                        if (name != "msub")
                            script.Add(Argument("sup", parts[name == "msubsup" ? 2 : 1], style, depth, token));
                        return [script];
                    case "munder" or "mover" or "munderover":
                        Require(parts, name == "munderover" ? 3 : 2);
                        if ((string?)element.Attribute(name == "munder" ? "accentunder" : "accent") == "true")
                        {
                            var mark = parts[1].Value;
                            return name switch
                            {
                                "munder" when mark is "_" or "¯" or "̲" =>
                                [
                                    new(Math + "bar", new XElement(Math + "barPr", Value("pos", "bot")),
                                        Argument("e", parts[0], style, depth, token)),
                                ],
                                "mover" when mark is "¯" or "‾" or "̅" =>
                                [
                                    new(Math + "bar", new XElement(Math + "barPr", Value("pos", "top")),
                                        Argument("e", parts[0], style, depth, token)),
                                ],
                                "mover" =>
                                [
                                    new(Math + "acc", new XElement(Math + "accPr", Value("chr", mark)),
                                        Argument("e", parts[0], style, depth, token)),
                                ],
                                _ => throw new NotSupportedException(
                                    "This MathML lower accent cannot be represented as an editable Word equation."),
                            };
                        }

                        return name switch
                        {
                            "munder" => [Limit("limLow", parts[0], parts[1], style, depth, token)],
                            "mover" => [Limit("limUpp", parts[0], parts[1], style, depth, token)],
                            _ =>
                            [
                                new(Math + "limUpp",
                                    new XElement(Math + "e", Limit("limLow", parts[0], parts[1], style, depth, token)),
                                    Argument("lim", parts[2], style, depth, token)),
                            ],
                        };
                    case "mtable": return [Matrix(element, style, depth, token)];
                    case "mfenced": return [Fence(element, style, depth, token)];
                    case "semantics":
                        var visible = parts.FirstOrDefault(part =>
                            part.Name.LocalName is not ("annotation" or "annotation-xml"));
                        element = visible ??
                                  throw new InvalidDataException("The MathML semantics element has no equation.");
                        depth++;
                        continue;
                    default:
                        throw new NotSupportedException(
                            $"The MathML element '{name}' cannot be represented as an editable Word equation.");
                }
            }
        }

        private static XElement Argument(string name, XElement source, EquationStyle style, int depth,
            CancellationToken token)
        {
            return new(Math + name, Map(source, style, depth + 1, token));
        }

        private static XElement Limit(string name, XElement source, XElement limit, EquationStyle style,
            int depth, CancellationToken token)
        {
            return new(Math + name, Argument("e", source, style, depth, token),
                Argument("lim", limit, style, depth, token));
        }

        private static XElement Value(string name, string value)
        {
            return new(Math + name, new XAttribute(Math + "val", value));
        }

        private static void Require(XElement[] parts, int count)
        {
            if (parts.Length != count)
                throw new InvalidDataException("The MathML equation has an invalid argument count.");
        }

        private static EquationStyle Inherit(XElement element, EquationStyle style)
        {
            return new((string?)element.Attribute("mathvariant") ?? style.Variant,
                (string?)element.Attribute("mathcolor") ?? style.Color,
                (string?)element.Attribute("mathbackground") ?? style.Background);
        }

        private static XElement Fence(XElement element, EquationStyle style, int depth, CancellationToken token)
        {
            var separators = ((string?)element.Attribute("separators") ?? ",").EnumerateRunes()
                .Where(rune => !Rune.IsWhiteSpace(rune)).ToArray();
            var content = new XElement(Math + "e");
            var index = 0;
            foreach (var part in element.Elements())
            {
                if (index > 0 && separators.Length > 0)
                    content.Add(Run(separators[System.Math.Min(index - 1, separators.Length - 1)].ToString(), style,
                        false));
                content.Add(Map(part, style, depth + 1, token));
                index++;
            }

            return new(Math + "d", new XElement(Math + "dPr",
                Value("begChr", (string?)element.Attribute("open") ?? "("),
                Value("sepChr", ""), Value("endChr", (string?)element.Attribute("close") ?? ")")), content);
        }
    }
}

using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media;
using HtmlAgilityPack;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedRichParser
    {
        [GeneratedRegex(@"([^{}]+)\{([^{}]*)\}")]
        private static partial Regex CssRulePattern();

        [GeneratedRegex(@"([.#]?[\w-]+)|([>+~])")]
        private static partial Regex SelectorPattern();

        private IEnumerable<(string Key, string Value)> Declarations(HtmlNode node)
        {
            foreach (var rule in _rules.Where(rule => MatchesSelector(node, rule.Selector))
                         .OrderBy(rule => Specificity(rule.Selector)))
            foreach (var declaration in SplitDeclarations(rule.Declarations))
                yield return declaration;
            foreach (var declaration in SplitDeclarations(node.GetAttributeValue("style", "")))
                yield return declaration;
        }

        private static IEnumerable<(string Key, string Value)> SplitDeclarations(string declarations)
        {
            foreach (var declaration in declarations.Split(';'))
            {
                var colon = declaration.IndexOf(':');
                if (colon <= 0) continue;
                var value = declaration[(colon + 1)..].Trim();
                if (value.EndsWith("!important", StringComparison.OrdinalIgnoreCase)) value = value[..^10].TrimEnd();
                yield return (declaration[..colon].Trim().ToLowerInvariant(), value);
            }
        }

        private static int Specificity(string selector)
        {
            return selector.Count(c => c == '#') * 100 +
                   selector.Count(c => c == '.') * 10 + selector.Count(c => c is ' ' or '>') + 1;
        }

        private static bool MatchesSelector(HtmlNode node, string selector)
        {
            if (selector.Length == 0 || selector.Contains(':') || selector.Contains('[') ||
                selector.Contains('@')) return false;
            var parts = selector.Replace(">", " > ", StringComparison.Ordinal)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = node;
            for (var index = parts.Length - 1; index >= 0; index--)
            {
                if (!MatchesSimple(current, parts[index])) return false;
                if (index == 0) return true;
                HtmlNode? ancestor;
                if (parts[index - 1] == ">")
                {
                    index--;
                    ancestor = current.Ancestors().FirstOrDefault();
                }
                else
                {
                    var next = parts[index - 1];
                    ancestor = current.Ancestors().FirstOrDefault(candidate => MatchesSimple(candidate, next));
                }

                if (ancestor is null) return false;
                current = ancestor;
            }

            return true;
        }

        private static bool MatchesSimple(HtmlNode node, string selector)
        {
            if (selector == "*") return true;
            var tokens = SelectorPattern().Matches(selector);
            if (tokens.Count == 0 || tokens.Sum(token => token.Length) != selector.Length) return false;
            foreach (Match token in tokens)
            {
                var text = token.Value;
                switch (text[0])
                {
                    case '#' when node.GetAttributeValue("id", "") != text[1..]:
                    case '.' when !node.GetAttributeValue("class", "")
                        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                        .Contains(text[1..], StringComparer.Ordinal):
                    case not ('#' or '.') when !text.Equals(node.Name, StringComparison.OrdinalIgnoreCase):
                        return false;
                }
            }

            return true;
        }

        private PinnedRichStyle TextStyle(HtmlNode node, PinnedRichStyle inherited)
        {
            var style = node.Name switch
            {
                "b" or "strong" => inherited with { Weight = FontWeight.Bold },
                "i" or "em" or "cite" or "dfn" or "var" => inherited with { Slant = FontStyle.Italic },
                "u" => inherited with { Underline = true },
                "s" or "strike" or "del" => inherited with { Strike = true },
                "code" or "tt" or "kbd" or "samp" => inherited with { FontFamily = "Consolas" },
                "pre" => inherited with { FontFamily = "Consolas", PreserveWhitespace = true, NoWrap = true },
                "nobr" => inherited with { NoWrap = true },
                "small" => inherited with { FontSize = inherited.FontSize * 0.8 },
                "big" => inherited with { FontSize = inherited.FontSize * 1.2 },
                "sub" => inherited with { FontSize = inherited.FontSize * 0.8, Baseline = BaselineAlignment.Subscript },
                "sup" => inherited with
                {
                    FontSize = inherited.FontSize * 0.8, Baseline = BaselineAlignment.Superscript,
                },
                "h1" => inherited with { Weight = FontWeight.Bold, FontSize = 28 },
                "h2" => inherited with { Weight = FontWeight.Bold, FontSize = 22 },
                "h3" => inherited with { Weight = FontWeight.Bold, FontSize = 18 },
                "h4" or "th" => inherited with { Weight = FontWeight.Bold },
                "h5" => inherited with { Weight = FontWeight.Bold, FontSize = 12 },
                "h6" => inherited with { Weight = FontWeight.Bold, FontSize = 10 },
                "a" => inherited with { Link = node.GetAttributeValue("href", ""), Underline = true },
                _ => inherited,
            };
            if (Color.TryParse(node.GetAttributeValue("color", ""), out var color))
                style = style with { Foreground = color };
            if (node.GetAttributeValue("face", "") is { Length: > 0 } family)
                style = style with { FontFamily = family };
            if (node.Name == "font" && int.TryParse(node.GetAttributeValue("size", ""), out var fontSize))
            {
                double[] htmlSizes = [8, 10, 12, 14, 18, 24, 36];
                style = style with { FontSize = htmlSizes[Math.Clamp(fontSize, 1, 7) - 1] };
            }

            foreach (var (key, value) in Declarations(node))
                switch (key)
                {
                    case "color"
                        when Color.TryParse(value, out var parsed): style = style with { Foreground = parsed }; break;
                    case "background" or "background-color" when Color.TryParse(value, out var parsed):
                        style = style with { Background = parsed }; break;
                    case "font-family":
                        style = style with { FontFamily = value.Split(',')[0].Trim().Trim('\'', '"') }; break;
                    case "font-size":
                        var size = FontSize(value, inherited.FontSize);
                        if (size > 0) style = style with { FontSize = size };
                        break;
                    case "font-weight":
                        if (value.Equals("bold", StringComparison.OrdinalIgnoreCase))
                            style = style with { Weight = FontWeight.Bold };
                        else if (value.Equals("normal", StringComparison.OrdinalIgnoreCase))
                            style = style with { Weight = FontWeight.Normal };
                        else if (int.TryParse(value, out var weight) && weight is >= 1 and <= 1000)
                            style = style with { Weight = (FontWeight)weight };
                        break;
                    case "font-style":
                        style = style with
                        {
                            Slant = value is "italic" or "oblique" ? FontStyle.Italic : FontStyle.Normal,
                        }; break;
                    case "text-decoration":
                        style = style with
                        {
                            Underline = value.Contains("underline", StringComparison.Ordinal),
                            Strike = value.Contains("line-through", StringComparison.Ordinal),
                        }; break;
                    case "white-space":
                        style = style with
                        {
                            PreserveWhitespace = value is "pre" or "pre-wrap", NoWrap = value is "pre" or "nowrap",
                        }; break;
                    case "vertical-align" when value is "sub" or "super":
                        style = style with
                        {
                            Baseline = value == "sub" ? BaselineAlignment.Subscript : BaselineAlignment.Superscript,
                        }; break;
                }

            return style;
        }

        private PinnedRichBlockStyle BlockStyle(HtmlNode node, PinnedRichBlockStyle inherited)
        {
            var style = new PinnedRichBlockStyle
                { Alignment = inherited.Alignment, Direction = inherited.Direction, Indent = inherited.Indent };
            style = node.Name switch
            {
                "center" => style with { Alignment = TextAlignment.Center },
                "blockquote" => style with { Indent = inherited.Indent + 40 },
                _ => style,
            };
            if (Color.TryParse(node.GetAttributeValue("bgcolor", ""), out var color))
                style = style with { Background = color };
            style = style with
            {
                Width = Length(node.GetAttributeValue("width", "")),
                Height = Length(node.GetAttributeValue("height", "")),
                Alignment = Alignment(node.GetAttributeValue("align", ""), style.Alignment),
                Direction = node.GetAttributeValue("dir", "") switch
                {
                    "rtl" => FlowDirection.RightToLeft, "ltr" => FlowDirection.LeftToRight, _ => style.Direction,
                },
            };
            if (Number(node.GetAttributeValue("border", "")) is { } border) style = style with { Border = new(border) };
            foreach (var (key, value) in Declarations(node))
                switch (key)
                {
                    case "background" or "background-color"
                        when Color.TryParse(value, out var parsed): style = style with { Background = parsed }; break;
                    case "text-align": style = style with { Alignment = Alignment(value, style.Alignment) }; break;
                    case "direction":
                        style = style with
                        {
                            Direction = value == "rtl" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                        }; break;
                    case "width": style = style with { Width = Length(value) }; break;
                    case "height": style = style with { Height = Length(value) }; break;
                    case "margin": style = style with { Margin = Thickness(value) }; break;
                    case "padding": style = style with { Padding = Thickness(value) }; break;
                    case "margin-top":
                        style = style with
                        {
                            Margin = new(style.Margin.Left, Number(value) ?? 0, style.Margin.Right,
                                style.Margin.Bottom),
                        }; break;
                    case "margin-right":
                        style = style with
                        {
                            Margin = new(style.Margin.Left, style.Margin.Top, Number(value) ?? 0,
                                style.Margin.Bottom),
                        }; break;
                    case "margin-bottom":
                        style = style with
                        {
                            Margin = new(style.Margin.Left, style.Margin.Top, style.Margin.Right,
                                Number(value) ?? 0),
                        }; break;
                    case "margin-left":
                        style = style with
                        {
                            Margin = new(Number(value) ?? 0, style.Margin.Top, style.Margin.Right,
                                style.Margin.Bottom),
                        }; break;
                    case "text-indent": style = style with { Indent = (Number(value) ?? 0) + inherited.Indent }; break;
                    case "border-color"
                        when Color.TryParse(value, out var parsed): style = style with { BorderColor = parsed }; break;
                    case "border-width": style = style with { Border = Thickness(value) }; break;
                    case "border":
                        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            if (Number(part) is { } width) style = style with { Border = new(width) };
                            else if (Color.TryParse(part, out var borderColor))
                                style = style with { BorderColor = borderColor };
                        break;
                    case "line-height":
                        var lineHeight = FontSize(value, 14);
                        if (lineHeight > 0) style = style with { LineHeight = lineHeight };
                        break;
                }

            return style;
        }

        private static TextAlignment Alignment(string value, TextAlignment fallback)
        {
            return value.ToLowerInvariant() switch
            {
                "left" => TextAlignment.Left, "right" => TextAlignment.Right, "center" => TextAlignment.Center,
                "justify" => TextAlignment.Justify, _ => fallback,
            };
        }

        private static double? Number(string value)
        {
            value = value.Trim();
            var factor = value.EndsWith("pt", StringComparison.OrdinalIgnoreCase) ? 96d / 72 : 1;
            if (value.EndsWith("pt", StringComparison.OrdinalIgnoreCase) ||
                value.EndsWith("px", StringComparison.OrdinalIgnoreCase)) value = value[..^2];
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
                   double.IsFinite(number)
                ? number * factor
                : null;
        }

        private static PinnedRichLength Length(string value)
        {
            value = value.Trim();
            var percent = value.EndsWith('%');
            return Number(percent ? value[..^1] : value) is { } number and >= 0
                ? new(number, percent)
                : PinnedRichLength.Auto;
        }

        private static double FontSize(string value, double inherited)
        {
            value = value.Trim();
            if (value.EndsWith('%')) return (Number(value[..^1]) ?? 0) * inherited / 100;
            if (value.EndsWith("em", StringComparison.OrdinalIgnoreCase)) return (Number(value[..^2]) ?? 0) * inherited;
            return Number(value) ?? 0;
        }

        private static Thickness Thickness(string value)
        {
            var values = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(part => Number(part) ?? 0)
                .ToArray();
            return values.Length switch
            {
                1 => new(values[0]), 2 => new(values[1], values[0]),
                3 => new(values[1], values[0], values[1], values[2]),
                >= 4 => new(values[3], values[0], values[1], values[2]), _ => default,
            };
        }
    }
}

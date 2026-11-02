using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedRichParser
    {
        internal const int MaximumNestingDepth = 64;

        private static readonly HashSet<string> Blocks =
            ["p", "div", "center", "address", "blockquote", "pre", "h1", "h2", "h3", "h4", "h5", "h6", "dd", "dt"];

        private readonly StringBuilder _plain = new();
        private readonly List<(string Selector, string Declarations)> _rules = [];

        internal PinnedRichDocument Parse(string html, Uri? baseUri = null)
        {
            var source = ReadHtml(html);
            if (PinnedHtmlSelection.FromMarkers(html) is { } selection)
                source = ReadHtml(selection.Extract(html));
            return ParseDocument(source, baseUri);
        }

        internal PinnedRichDocument ParseFragment(string html, PinnedHtmlSelection selection, Uri? baseUri = null)
        {
            _ = ReadHtml(html);
            return ParseDocument(ReadHtml(selection.Extract(html)), baseUri);
        }

        private PinnedRichDocument ParseDocument(HtmlDocument source, Uri? baseUri)
        {
            var declaredBase = source.DocumentNode.Descendants("base").FirstOrDefault()?.GetAttributeValue("href", "");
            if (Uri.TryCreate(declaredBase, UriKind.Absolute, out var absoluteBase) &&
                absoluteBase.Scheme is "http" or "https" or "file")
                baseUri = absoluteBase;
            else if (baseUri is not null && !string.IsNullOrEmpty(declaredBase) &&
                     Uri.TryCreate(baseUri, declaredBase, out var relativeBase) &&
                     relativeBase.Scheme is "http" or "https" or "file")
                baseUri = relativeBase;
            _plain.Clear();
            _rules.Clear();
            foreach (var sheet in source.DocumentNode.Descendants("style"))
            foreach (Match rule in CssRulePattern().Matches(sheet.InnerText))
            foreach (var selector in rule.Groups[1].Value.Split(','))
                _rules.Add((selector.Trim(), rule.Groups[2].Value));
            var body = source.DocumentNode.Descendants("body").FirstOrDefault() ?? source.DocumentNode;
            var rootStyle = TextStyle(body, new());
            var rootBlock = BlockStyle(body, new());
            var blocks = new List<PinnedRichBlock>();
            ParseContainer(body, rootStyle, ContentBlockStyle(rootBlock), blocks);
            if (_plain.Length != 0 && _plain[^1] == '\n') _plain.Length--;
            return new(_plain.ToString(), blocks, baseUri, rootBlock);
        }

        internal static HtmlDocument ReadHtml(string html)
        {
            var source = new HtmlDocument { OptionMaxNestedChildNodes = MaximumNestingDepth };
            try
            {
                source.LoadHtml(html);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                throw new PinnedHtmlFormatException(error);
            }

            return source;
        }

        private void ParseContainer(HtmlNode node, PinnedRichStyle style, PinnedRichBlockStyle block,
            List<PinnedRichBlock> target, string? prefix = null)
        {
            var runs = new List<PinnedRichRun>();
            var start = _plain.Length;
            if (prefix is not null) AddText(prefix, style, runs, false);
            foreach (var child in node.ChildNodes)
            {
                if (child.NodeType == HtmlNodeType.Comment ||
                    child.Name is "head" or "style" or "script" or "title" or "meta" or "iframe" or "object" or "embed"
                        or "noscript")
                    continue;
                switch (child.Name)
                {
                    case "table":
                        Flush();
                        target.Add(ParseTable(child, style, block));
                        start = _plain.Length;
                        break;
                    case "ul" or "ol" or "dl":
                        Flush();
                        ParseList(child, style, block, target);
                        start = _plain.Length;
                        break;
                    case "hr":
                        Flush();
                        target.Add(new PinnedRichRule(_plain.Length, BlockStyle(child, block)));
                        _plain.Append('\n');
                        start = _plain.Length;
                        break;
                    case var name when Blocks.Contains(name):
                    {
                        Flush();
                        var before = _plain.Length;
                        var childStyle = BlockStyle(child, block);
                        var content = new List<PinnedRichBlock>();
                        ParseContainer(child, TextStyle(child, style), ContentBlockStyle(childStyle), content);
                        if (_plain.Length == before)
                        {
                            content.Add(new PinnedRichParagraph(before, 0, ContentBlockStyle(childStyle), []));
                            _plain.Append('\n');
                        }

                        target.Add(new PinnedRichContainer(before, _plain.Length - before, childStyle, content));
                        start = _plain.Length;
                        break;
                    }
                    default:
                        ParseInline(child, style, runs);
                        break;
                }
            }

            Flush();
            return;

            void Flush()
            {
                if (runs.Count == 0) return;
                target.Add(new PinnedRichParagraph(start, _plain.Length - start, block, [.. runs]));
                _plain.Append('\n');
                runs.Clear();
                start = _plain.Length;
            }
        }

        private static PinnedRichBlockStyle ContentBlockStyle(PinnedRichBlockStyle container)
        {
            return new()
            {
                Alignment = container.Alignment, Direction = container.Direction, LineHeight = container.LineHeight,
            };
        }

        private void ParseInline(HtmlNode node, PinnedRichStyle inherited, List<PinnedRichRun> runs)
        {
            if (node.NodeType == HtmlNodeType.Comment ||
                node.Name is "script" or "style" or "iframe" or "object" or "embed") return;
            if (node.NodeType == HtmlNodeType.Text)
            {
                AddText(WebUtility.HtmlDecode(node.InnerText), inherited, runs, true);
                return;
            }

            var style = TextStyle(node, inherited);
            switch (node.Name)
            {
                case "br":
                    AddText("\n", style, runs, false);
                    return;
                case "img":
                {
                    var source = node.GetAttributeValue("src", node.GetAttributeValue("source", ""));
                    var image = new PinnedRichImage(source, WebUtility.HtmlDecode(node.GetAttributeValue("alt", "")),
                        Length(node.GetAttributeValue("width", "")), Length(node.GetAttributeValue("height", "")));
                    foreach (var (key, value) in Declarations(node))
                        image = key switch
                        {
                            "width" => image with { Width = Length(value) },
                            "height" => image with { Height = Length(value) },
                            _ => image,
                        };
                    runs.Add(new(_plain.Length, "\ufffc", style, image));
                    _plain.Append('\ufffc');
                    return;
                }
            }

            foreach (var child in node.ChildNodes) ParseInline(child, style, runs);
        }

        private void AddText(string text, PinnedRichStyle style, List<PinnedRichRun> runs, bool normalize)
        {
            if (normalize && !style.PreserveWhitespace)
            {
                var normalized = new StringBuilder(text.Length);
                var space = runs.Count == 0 || (_plain.Length != 0 && char.IsWhiteSpace(_plain[^1]));
                foreach (var character in text)
                    if (character is ' ' or '\t' or '\r' or '\n' or '\f')
                    {
                        if (!space) normalized.Append(' ');
                        space = true;
                    }
                    else
                    {
                        normalized.Append(character);
                        space = false;
                    }

                text = normalized.ToString();
            }

            if (text.Length == 0) return;
            runs.Add(new(_plain.Length, text, style));
            _plain.Append(text);
        }

        private void ParseList(HtmlNode list, PinnedRichStyle inherited, PinnedRichBlockStyle block,
            List<PinnedRichBlock> target)
        {
            var ordered = list.Name == "ol";
            var reversed = ordered && list.Attributes.Contains("reversed");
            var step = reversed ? -1 : 1;
            long ordinal =
                list.GetAttributeValue("start", reversed ? list.ChildNodes.Count(node => node.Name == "li") : 1);
            var style = TextStyle(list, inherited);
            foreach (var item in list.ChildNodes.Where(item => item.Name is "li" or "dd" or "dt"))
            {
                if (long.TryParse(item.GetAttributeValue("value", ""), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var explicitValue)) ordinal = explicitValue;
                var marker = item.Name != "li"
                    ? null
                    : ordered
                        ? ListMarker(ordinal, list.GetAttributeValue("type", "1")) + ". "
                        : "• ";
                var itemBlock = BlockStyle(item, block) with { Indent = block.Indent + (item.Name == "dt" ? 0 : 20) };
                ParseContainer(item, TextStyle(item, style), itemBlock, target, marker);
                ordinal += step;
            }
        }

        private static string ListMarker(long ordinal, string type)
        {
            if (ordinal <= 0) return ordinal.ToString(CultureInfo.InvariantCulture);
            switch (type)
            {
                case "a" or "A":
                {
                    var text = new StringBuilder();
                    for (var value = ordinal; value > 0; value = (value - 1) / 26)
                        text.Insert(0, (char)((type == "a" ? 'a' : 'A') + (value - 1) % 26));
                    return text.ToString();
                }
                case "i" or "I" when ordinal <= 3999:
                {
                    (int Value, string Symbol)[] numerals =
                    [
                        (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"),
                        (40, "XL"),
                        (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
                    ];
                    var text = new StringBuilder();
                    foreach (var (value, symbol) in numerals)
                        while (ordinal >= value)
                        {
                            text.Append(symbol);
                            ordinal -= value;
                        }

                    return type == "i" ? text.ToString().ToLowerInvariant() : text.ToString();
                }
            }

            return ordinal.ToString(CultureInfo.InvariantCulture);
        }
    }
}

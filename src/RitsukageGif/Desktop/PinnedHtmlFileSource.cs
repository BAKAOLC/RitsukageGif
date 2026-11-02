using System.Net;
using HtmlAgilityPack;

namespace RitsukageGif.Desktop
{
    internal static class PinnedHtmlFileSource
    {
        internal static string WithBase(string html, string? sourceUrl)
        {
            if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var source) ||
                source.Scheme is not ("file" or "http" or "https")) return html;
            var document = PinnedRichParser.ReadHtml(html);
            if (document.DocumentNode.Descendants("base").Any()) return html;
            var declaration = "<base href=\"" + WebUtility.HtmlEncode(source.AbsoluteUri) + "\">";
            var root = document.DocumentNode.Descendants("html").FirstOrDefault();
            if (root is null) return declaration + html;
            var head = root.ChildNodes.FirstOrDefault(node => node.Name == "head");
            if (head is null)
            {
                head = document.CreateElement("head");
                root.PrependChild(head);
            }

            head.PrependChild(HtmlNode.CreateNode(declaration));
            return document.DocumentNode.OuterHtml;
        }
    }
}

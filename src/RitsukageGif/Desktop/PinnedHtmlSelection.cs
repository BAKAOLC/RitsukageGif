namespace RitsukageGif.Desktop
{
    internal sealed record PinnedHtmlSelection(int Start, int Length, bool HasContext = true)
    {
        internal bool IsValidFor(string html)
        {
            return Start >= 0 && Length >= 0 && Start <= html.Length - Length;
        }

        internal string Extract(string html)
        {
            return html.Substring(Start, Length);
        }

        internal static PinnedHtmlSelection? FromMarkers(string html)
        {
            const string marker = "<!--StartFragment-->";
            var start = html.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return null;
            var end = html.IndexOf("<!--EndFragment-->", StringComparison.Ordinal);
            if (end <= start) end = html.Length;
            start += marker.Length;
            return new(start, end - start);
        }
    }
}

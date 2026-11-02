using System.Globalization;
using System.Net;
using System.Text;

namespace RitsukageGif.Desktop
{
    internal static class ClipboardHtml
    {
        public static string CreateFragment(string path)
        {
            return "<img src=\"" + WebUtility.HtmlEncode(new Uri(Path.GetFullPath(path)).AbsoluteUri) + "\">";
        }

        public static byte[] CreateWindows(string fragment, string? sourceUrl = null)
        {
            const string startMarker = "<!--StartFragment-->";
            const string endMarker = "<!--EndFragment-->";
            if (PinnedHtmlSelection.FromMarkers(fragment) is { } selection)
                return CreateWindowsContext(fragment, selection, sourceUrl);
            var document = fragment.AsSpan().TrimStart();
            var completeDocument = document.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
                                   document.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase);
            var prefix = completeDocument ? startMarker : "<html><body>" + startMarker;
            var suffix = completeDocument ? endMarker : endMarker + "</body></html>";
            return CreateWindowsContext(prefix + fragment + suffix, new(prefix.Length, fragment.Length), sourceUrl);
        }

        internal static byte[] CreateWindowsContext(string context, PinnedHtmlSelection selection,
            string? sourceUrl = null)
        {
            var sourceHeader = "";
            if (sourceUrl is not null)
            {
                if (sourceUrl.IndexOfAny(['\r', '\n']) >= 0 ||
                    !Uri.TryCreate(sourceUrl, UriKind.Absolute, out var source) ||
                    source.Scheme is not ("file" or "http" or "https"))
                    throw new ArgumentException(
                        "CF_HTML SourceURL must be an absolute file or HTTP URI without line breaks.",
                        nameof(sourceUrl));
                sourceHeader = "SourceURL:" + source.AbsoluteUri + "\r\n";
            }

            var contextHeader = selection.HasContext
                ? "StartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\n"
                : "StartHTML:-1\r\nEndHTML:-1\r\n";
            var template = "Version:1.0\r\n" + contextHeader +
                           "StartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n" + sourceHeader;
            var startHtml = Encoding.UTF8.GetByteCount(string.Format(CultureInfo.InvariantCulture,
                template, 0, 0, 0, 0));
            var startFragment = startHtml + Encoding.UTF8.GetByteCount(context.AsSpan(0, selection.Start));
            var endFragment = startFragment +
                              Encoding.UTF8.GetByteCount(context.AsSpan(selection.Start, selection.Length));
            var endHtml = startHtml + Encoding.UTF8.GetByteCount(context);
            var header = string.Format(CultureInfo.InvariantCulture, template,
                startHtml, endHtml, startFragment, endFragment);
            return Encoding.UTF8.GetBytes(header + context + '\0');
        }
    }
}

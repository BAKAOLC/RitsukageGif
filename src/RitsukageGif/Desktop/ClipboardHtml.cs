using System.Net;
using System.Text;

namespace RitsukageGif.Desktop
{
    internal static class ClipboardHtml
    {
        public static string CreateFragment(string path) =>
            "<img src=\"" + WebUtility.HtmlEncode(new Uri(Path.GetFullPath(path)).AbsoluteUri) + "\">";

        public static byte[] CreateWindows(string fragment)
        {
            const string prefix = "<html><body><!--StartFragment-->";
            const string suffix = "<!--EndFragment--></body></html>";
            const string template =
                "Version:1.0\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
            var startHtml = Encoding.UTF8.GetByteCount(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                template, 0, 0, 0, 0));
            var startFragment = startHtml + Encoding.UTF8.GetByteCount(prefix);
            var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
            var endHtml = endFragment + Encoding.UTF8.GetByteCount(suffix);
            var header = string.Format(System.Globalization.CultureInfo.InvariantCulture, template,
                startHtml, endHtml, startFragment, endFragment);
            return Encoding.UTF8.GetBytes(header + prefix + fragment + suffix + '\0');
        }
    }
}

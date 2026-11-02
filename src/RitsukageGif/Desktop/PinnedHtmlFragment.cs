using System.Globalization;
using System.Text;

namespace RitsukageGif.Desktop
{
    internal static class PinnedHtmlFragment
    {
        internal static string? SourceUrl(ReadOnlySpan<byte> bytes)
        {
            var headerLength = bytes.IndexOf((byte)'<');
            if (headerLength < 0) return null;
            var header = Encoding.UTF8.GetString(bytes[..headerLength]);
            foreach (var line in header.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.StartsWith("SourceURL:", StringComparison.OrdinalIgnoreCase)) continue;
                var value = line[10..].Trim();
                return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "file" or "http" or "https"
                    ? uri.AbsoluteUri
                    : null;
            }

            return null;
        }

        public static (string Html, PinnedHtmlSelection Selection) ReadWindows(ReadOnlySpan<byte> bytes)
        {
            var headerLength = bytes.IndexOf((byte)'<');
            if (headerLength < 0) throw new InvalidDataException("CF_HTML has no document.");
            var header = Encoding.ASCII.GetString(bytes[..headerLength]);
            var start = Offset(header, "StartFragment:");
            var end = Offset(header, "EndFragment:");
            if (start < 0 || end < start || end > bytes.Length)
                throw new InvalidDataException("CF_HTML fragment offsets are invalid.");
            var contextStart = Offset(header, "StartHTML:");
            var contextEnd = Offset(header, "EndHTML:");
            var encoding = new UTF8Encoding(false, true);
            if (contextStart == -1 && contextEnd == -1)
            {
                var fragment = encoding.GetString(bytes[start..end]);
                return (fragment, new(0, fragment.Length, false));
            }

            if (contextStart < headerLength || contextEnd < contextStart || contextEnd > bytes.Length ||
                start < contextStart || end > contextEnd)
                throw new InvalidDataException("CF_HTML context offsets are invalid.");
            var html = encoding.GetString(bytes[contextStart..contextEnd]);
            return (html, new(encoding.GetCharCount(bytes[contextStart..start]),
                encoding.GetCharCount(bytes[start..end])));
        }

        private static int Offset(string header, string key)
        {
            var start = header.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return -1;
            var value = header.AsSpan(start + key.Length);
            var end = value.IndexOfAny('\r', '\n');
            if (end >= 0) value = value[..end];
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset)
                ? offset
                : -1;
        }
    }
}

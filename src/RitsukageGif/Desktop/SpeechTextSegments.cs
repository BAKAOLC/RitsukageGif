using System.Globalization;

namespace RitsukageGif.Desktop
{
    internal static class SpeechTextSegments
    {
        internal static IEnumerable<string> Split(string text)
        {
            var start = 0;
            while (start < text.Length)
            {
                var end = start;
                var preferred = start;
                while (end < text.Length)
                {
                    var size = StringInfo.GetNextTextElementLength(text.AsSpan(end));
                    if (end > start && end + size - start > 600) break;
                    end += size;
                    if (char.IsWhiteSpace(text[end - 1]) || ".!?;。！？；".Contains(text[end - 1])) preferred = end;
                }

                if (end < text.Length && preferred > start + (end - start) / 2) end = preferred;
                var segment = text[start..end].Trim();
                if (segment.Length > 0) yield return segment;
                start = end;
            }
        }
    }
}

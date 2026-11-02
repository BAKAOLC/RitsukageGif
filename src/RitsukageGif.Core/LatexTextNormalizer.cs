using System.Text;

namespace RitsukageGif.Core
{
    internal static class LatexTextNormalizer
    {
        internal static string Normalize(string value)
        {
            var result = new StringBuilder(value.Length);
            var depth = 0;
            var textDepth = -1;
            var textPending = false;
            var controlWordEnd = -1;
            for (var index = 0; index < value.Length; index++)
            {
                var current = value[index];
                if (current == '\\')
                {
                    result.Append(current);
                    if (index + 1 < value.Length && !char.IsAsciiLetter(value[index + 1]))
                    {
                        result.Append(value[++index]);
                        controlWordEnd = -1;
                        continue;
                    }

                    var start = index + 1;
                    while (index + 1 < value.Length && char.IsAsciiLetter(value[index + 1]))
                        result.Append(value[++index]);
                    var name = value.AsSpan(start, index - start + 1);
                    textPending = name is "text" or "textrm" or "textsf" or "texttt" or "textbf" or "textit"
                        or "operatorname";
                    controlWordEnd = result.Length;
                    continue;
                }

                if (char.IsWhiteSpace(current))
                {
                    while (index + 1 < value.Length && char.IsWhiteSpace(value[index + 1])) index++;
                    if (textDepth >= 0 || (controlWordEnd == result.Length && index + 1 < value.Length &&
                                           char.IsAsciiLetter(value[index + 1]))) result.Append(' ');
                    continue;
                }

                switch (current)
                {
                    case '{':
                        depth++;
                        if (textPending && textDepth < 0) textDepth = depth;
                        break;
                    case '}':
                        if (depth == textDepth) textDepth = -1;
                        depth--;
                        break;
                }

                result.Append(current);
                textPending = false;
                controlWordEnd = -1;
            }

            return result.ToString();
        }
    }
}

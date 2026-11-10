using System.Text;

namespace RitsukageGif.Core
{
    public static class LatexTextNormalizer
    {
        public static string RemoveInsignificantSpaces(string value)
        {
            if (HasUnknownEnvironment(value)) return value;
            var result = new StringBuilder(value.Length);
            var depth = 0;
            var literalDepth = -1;
            var literalPending = false;
            var controlWordEnd = -1;
            for (var index = 0; index < value.Length; index++)
            {
                var current = value[index];
                if (current == '%')
                {
                    do result.Append(value[index++]);
                    while (index < value.Length && value[index] is not ('\r' or '\n'));
                    index--;
                    controlWordEnd = -1;
                    continue;
                }

                if (current == '\\')
                {
                    var start = index++;
                    if (index >= value.Length)
                    {
                        result.Append('\\');
                        break;
                    }

                    if (!char.IsAsciiLetter(value[index]))
                    {
                        result.Append(value.AsSpan(start, 2));
                        controlWordEnd = -1;
                        continue;
                    }

                    while (index + 1 < value.Length && char.IsAsciiLetter(value[index + 1])) index++;
                    var command = value.AsSpan(start + 1, index - start);
                    result.Append(value.AsSpan(start, index - start + 1));
                    if (command is "verb")
                    {
                        if (index + 1 < value.Length && value[index + 1] == '*') result.Append(value[++index]);
                        if (index + 1 < value.Length && value[index + 1] is not ('\r' or '\n'))
                        {
                            var delimiter = value[++index];
                            result.Append(delimiter);
                            while (index + 1 < value.Length && value[index + 1] is not ('\r' or '\n'))
                            {
                                var character = value[++index];
                                result.Append(character);
                                if (character == delimiter) break;
                            }
                        }

                        controlWordEnd = -1;
                        continue;
                    }

                    literalPending = command is not ("frac" or "dfrac" or "tfrac" or "binom" or "dbinom" or
                        "tbinom" or "sqrt" or "overset" or "underset" or "stackrel" or "mathbb" or "mathcal" or
                        "mathfrak" or "mathbf" or "mathrm" or "mathsf" or "mathtt" or "mathit" or "boldsymbol");
                    controlWordEnd = result.Length;
                    continue;
                }

                if (char.IsWhiteSpace(current))
                {
                    if (literalDepth >= 0 || current is '\r' or '\n')
                    {
                        result.Append(current);
                    }
                    else if (controlWordEnd == result.Length)
                    {
                        var next = index + 1;
                        while (next < value.Length && char.IsWhiteSpace(value[next])) next++;
                        if (next < value.Length && char.IsAsciiLetter(value[next])) result.Append(' ');
                    }

                    continue;
                }

                if (current == '*' && literalPending)
                {
                    result.Append(current);
                    continue;
                }

                var literalFollowing = false;
                switch (current)
                {
                    case '{':
                        depth++;
                        if (literalPending && literalDepth < 0) literalDepth = depth;
                        break;
                    case '}':
                        if (literalDepth == depth)
                        {
                            literalDepth = -1;
                            literalFollowing = true;
                        }

                        depth--;
                        break;
                }

                result.Append(current);
                literalPending = literalFollowing;
                controlWordEnd = -1;
            }

            return result.ToString();
        }

        private static bool HasUnknownEnvironment(string value)
        {
            var start = 0;
            while ((start = value.IndexOf(@"\begin", start, StringComparison.Ordinal)) >= 0)
            {
                var preceding = start - 1;
                while (preceding >= 0 && value[preceding] == '\\') preceding--;
                var index = start + 6;
                if ((start - preceding - 1) % 2 != 0 ||
                    (index < value.Length && char.IsAsciiLetter(value[index])))
                {
                    start = index;
                    continue;
                }

                while (index < value.Length && char.IsWhiteSpace(value[index])) index++;
                if (index >= value.Length || value[index] != '{') return true;
                var end = value.IndexOf('}', index + 1);
                if (end < 0) return true;
                var environment = value.AsSpan(index + 1, end - index - 1).Trim();
                if (environment is not ("math" or "displaymath" or "equation" or "equation*" or
                    "align" or "align*" or "aligned" or "alignedat" or "gather" or "gather*" or "gathered" or
                    "multline" or "multline*" or "split" or "array" or "matrix" or "pmatrix" or "bmatrix" or
                    "Bmatrix" or "vmatrix" or "Vmatrix" or "smallmatrix" or "cases")) return true;
                start = end + 1;
            }

            return false;
        }

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

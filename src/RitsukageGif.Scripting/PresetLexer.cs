using System.Text;

namespace RitsukageGif.Scripting
{
    internal static class PresetLexer
    {
        public static IReadOnlyList<PresetToken> Scan(string source, bool tolerant = false)
        {
            if (source.Length > 262144) throw new PresetSyntaxException(1, 1, UiText.Get("DslTooComplex"));
            var tokens = new List<PresetToken>();
            int at = 0, line = 1, column = 1;

            while (at < source.Length)
            {
                var c = source[at];
                if (char.IsWhiteSpace(c))
                {
                    Take();
                    continue;
                }

                if (c == '#' || (c == '/' && at + 1 < source.Length && source[at + 1] == '/'))
                {
                    while (at < source.Length && source[at] != '\n') Take();
                    continue;
                }

                var startLine = line;
                var startColumn = column;
                var text = new StringBuilder();
                if (c == '/' && at + 1 < source.Length && source[at + 1] == '*')
                {
                    Take();
                    Take();
                    while (at + 1 < source.Length && !(source[at] == '*' && source[at + 1] == '/')) Take();
                    if (at + 1 >= source.Length)
                    {
                        if (tolerant) break;
                        throw new PresetSyntaxException(startLine, startColumn,
                            UiText.Format("DslExpected", "*/", "<eof>"));
                    }

                    Take();
                    Take();
                    continue;
                }

                if (c is '"' or '\'')
                {
                    var quote = Take();
                    var closed = false;
                    while (at < source.Length)
                    {
                        c = Take();
                        if (c == quote)
                        {
                            closed = true;
                            break;
                        }

                        if (c == '\\')
                        {
                            if (at == source.Length) break;
                            c = Take() switch
                            {
                                '\'' => '\'', 'n' => '\n', 'r' => '\r', 't' => '\t', '"' => '"', '\\' => '\\',
                                _ when tolerant => '\0',
                                _ => throw new PresetSyntaxException(line, column, UiText.Get("DslInvalidEscape")),
                            };
                        }

                        text.Append(c);
                    }

                    if (!closed && !tolerant)
                        throw new PresetSyntaxException(startLine, startColumn, UiText.Get("DslUnclosedString"));
                    tokens.Add(new(text.ToString(), startLine, startColumn, true));
                }
                else if (char.IsLetter(c) || c == '_')
                {
                    while (at < source.Length && (char.IsLetterOrDigit(source[at]) || source[at] == '_' ||
                                                  (source[at] == '.' && at + 1 < source.Length &&
                                                   (char.IsLetter(source[at + 1]) || source[at + 1] == '_'))))
                        text.Append(Take());
                    tokens.Add(new(text.ToString(), startLine, startColumn));
                }
                else if (char.IsAsciiDigit(c) ||
                         (c == '.' && at + 1 < source.Length && char.IsAsciiDigit(source[at + 1])))
                {
                    if (c == '0' && at + 1 < source.Length && source[at + 1] is 'x' or 'X' or 'b' or 'B')
                    {
                        text.Append(Take());
                        text.Append(Take());
                        while (at < source.Length && char.IsAsciiLetterOrDigit(source[at])) text.Append(Take());
                    }
                    else
                    {
                        while (at < source.Length && char.IsAsciiDigit(source[at])) text.Append(Take());
                        if (at < source.Length && source[at] == '.')
                        {
                            text.Append(Take());
                            while (at < source.Length && char.IsAsciiDigit(source[at])) text.Append(Take());
                        }

                        if (at < source.Length && source[at] is 'e' or 'E')
                        {
                            text.Append(Take());
                            if (at < source.Length && source[at] is '+' or '-') text.Append(Take());
                            while (at < source.Length && char.IsAsciiDigit(source[at])) text.Append(Take());
                        }
                    }

                    tokens.Add(new(text.ToString(), startLine, startColumn));
                }
                else
                {
                    text.Append(Take());
                    if (text[0] == '.' && at + 1 < source.Length && source[at] == '.' && source[at + 1] == '.')
                    {
                        text.Append(Take());
                        text.Append(Take());
                    }

                    if (at < source.Length &&
                        text.ToString() + source[at] is "->" or "==" or "!=" or "<=" or ">=" or "&&" or "||" or "+="
                            or "-=" or "*=" or "/=" or "%=" or "++" or "--" or "=>" or "??" or "?.")
                        text.Append(Take());
                    tokens.Add(new(text.ToString(), startLine, startColumn));
                }
            }

            tokens.Add(new("<eof>", line, column));
            return tokens;

            char Take()
            {
                var c = source[at++];
                if (c == '\n')
                {
                    line++;
                    column = 1;
                }
                else
                {
                    column++;
                }

                return c;
            }
        }
    }
}

namespace RitsukageGif.Desktop
{
    internal static class PinnedFormulaInput
    {
        public static PinnedFormulaDocument? TryCreate(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var source = text.AsSpan().Trim();
            if (!HasMathSyntax(source)) return null;
            return FormulaImageRenderer.TryValidate(text) ? new([text]) : null;
        }

        private static bool HasMathSyntax(ReadOnlySpan<char> source)
        {
            if (source is ['$', .., '$'] or ['\\', '(', .., '\\', ')'] or ['\\', '[', .., '\\', ']'])
                return true;
            for (var index = 0; index + 2 < source.Length; index++)
                if (char.IsAsciiLetter(source[index]) && source[index + 1] == ':' && source[index + 2] == '\\')
                    return false;
            for (var index = 0; index + 1 < source.Length; index++)
                if (source[index] == '\\' && char.IsAsciiLetter(source[index + 1]))
                    return true;
            return false;
        }
    }
}

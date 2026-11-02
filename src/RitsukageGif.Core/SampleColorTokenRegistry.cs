namespace RitsukageGif.Core
{
    public sealed partial class SampleColorTokenRegistry
    {
        private static readonly char[] Styles = "bxXhHfFpis".ToCharArray();
        private readonly Dictionary<string, SampleColorToken> _tokens = new(StringComparer.Ordinal);
        public static SampleColorTokenRegistry Default { get; } = CreateDefault();
        public IReadOnlyCollection<SampleColorToken> Tokens => _tokens.Values;

        // ReSharper disable once MemberCanBePrivate.Global
        public void Register(SampleColorToken token)
        {
            ArgumentNullException.ThrowIfNull(token);
            if (token.Id.Length != 2 || !token.Id.All(char.IsAsciiLetter))
                throw new ArgumentException("Color parameter IDs must contain two ASCII letters.", nameof(token));
            if (!token.SupportedStyles.Contains(token.DefaultStyle))
                throw new ArgumentException("The default color parameter style must be supported.", nameof(token));
            _tokens.Add(token.Id, token);
        }

        public bool TryCompile(string source, out SampleColorTemplate? result, out TemplateDiagnostic? error)
        {
            ArgumentNullException.ThrowIfNull(source);
            var segments = new List<(string Literal, SampleColorToken? Token, char Style)>();
            var literal = 0;
            for (var position = 0; position < source.Length; position++)
            {
                if (source[position] != '%') continue;
                if (position + 2 >= source.Length) continue;
                var id = source.Substring(position + 1, 2);
                if (!_tokens.TryGetValue(id, out var token))
                {
                    if (!char.IsAsciiLetter(source[position + 1])) continue;
                    result = null;
                    error = new(position, 3, $"Unknown color parameter: %{id}.");
                    return false;
                }

                if (position > literal) segments.Add((source[literal..position], null, '\0'));
                var length = 3;
                var style = token.DefaultStyle;
                if (position + 3 < source.Length && Array.IndexOf(Styles, source[position + 3]) >= 0)
                {
                    style = source[position + 3];
                    if (!token.SupportedStyles.Contains(style))
                    {
                        result = null;
                        error = new(position, 4, $"Unsupported format '{style}' for color parameter %{id}.");
                        return false;
                    }

                    length++;
                }

                segments.Add(("", token, style));
                position += length - 1;
                literal = position + 1;
            }

            if (literal < source.Length) segments.Add((source[literal..], null, '\0'));
            result = new(source, [.. segments]);
            error = null;
            return true;
        }

        private static SampleColorTokenRegistry CreateDefault()
        {
            var registry = new SampleColorTokenRegistry();
            registry.RegisterChannels();
            registry.RegisterColorSpaces();
            registry.RegisterContext();
            return registry;
        }
    }
}

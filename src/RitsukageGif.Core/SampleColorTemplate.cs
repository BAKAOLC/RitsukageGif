using System.Text;

namespace RitsukageGif.Core
{
    public sealed class SampleColorTemplate
    {
        private readonly (string Literal, SampleColorToken? Token, char Style)[] _segments;

        internal SampleColorTemplate(string source, (string Literal, SampleColorToken? Token, char Style)[] segments)
        {
            Source = source;
            _segments = segments;
            Inputs = segments.Aggregate(SampleColorTokenInputs.None,
                (inputs, segment) => inputs | (segment.Token?.Inputs ?? SampleColorTokenInputs.None));
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public string Source { get; }
        public SampleColorTokenInputs Inputs { get; }

        public string Format(SampleColorContext context)
        {
            var output = new StringBuilder(Source.Length + 16);
            var values = new SampleColorValues(context);
            foreach (var segment in _segments)
                output.Append(segment.Token is { } token ? token.Format(values, segment.Style) : segment.Literal);
            return output.ToString();
        }
    }
}

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace RitsukageGif.Desktop
{
    internal sealed class ProgramExclusionRules
    {
        private static readonly ConditionalWeakTable<List<string>, ProgramExclusionRules> Cache = new();
        private static readonly ConditionalWeakTable<string, Regex> Expressions = new();
        private readonly Regex[] _patterns;

        private ProgramExclusionRules(List<string> rules)
        {
            _patterns = [.. rules.Select(GetExpression)];
        }

        internal static StringComparer Comparer { get; } = OperatingSystem.IsLinux()
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;

        internal static bool Same(string? left, string? right)
        {
            return Comparer.Equals(left, right);
        }

        internal static List<string> Normalize(IEnumerable<string>? values)
        {
            var result = new List<string>();
            foreach (var value in values ?? [])
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            foreach (var part in (value ?? string.Empty).Split([',', ';', '，', '\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries))
                if (TryNormalize(part, out var rule) && !result.Contains(rule, Comparer))
                    result.Add(rule);
            return result;
        }

        internal static bool TryNormalize(string? value, out string rule)
        {
            rule = value?.Trim().Trim('"').Replace('\\', '/') ?? string.Empty;
            if (rule.Length is 0 or > 32767 || rule.Any(char.IsControl)) return false;
            try
            {
                _ = GetExpression(rule);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        internal static bool Matches(List<string> rules, string? program)
        {
            if (rules.Count == 0 || string.IsNullOrEmpty(program)) return false;
            var path = program.Contains('\\') ? program.Replace('\\', '/') : program;
            var compiled = Cache.GetValue(rules, static items => new(items));
            foreach (var pattern in compiled._patterns)
                if (pattern.IsMatch(path))
                    return true;
            return false;
        }

        private static Regex GetExpression(string rule)
        {
            return Expressions.GetValue(rule, ToExpression);
        }

        private static Regex ToExpression(string rule)
        {
            var expression = new StringBuilder(rule.Length + 8);
            for (var index = 0; index < rule.Length; index++)
                switch (rule[index])
                {
                    case '*': expression.Append(".*"); break;
                    case '?': expression.Append('.'); break;
                    case '[':
                        var start = index + 1;
                        if (start < rule.Length && rule[start] == '^') start++;
                        if (start < rule.Length && rule[start] == ']') start++;
                        var end = rule.IndexOf(']', start);
                        if (end < 0) throw new ArgumentException("Invalid wildcard character class.", nameof(rule));
                        expression.Append('[');
                        for (var item = index + 1; item < end; item++)
                        {
                            if (rule[item] is '[' or ']') expression.Append('\\');
                            expression.Append(rule[item]);
                        }

                        expression.Append(']');
                        index = end;
                        break;
                    default: expression.Append(Regex.Escape(rule[index].ToString())); break;
                }

            expression.Append("\\z");
            return new(expression.ToString(),
                (OperatingSystem.IsLinux() ? RegexOptions.None : RegexOptions.IgnoreCase) |
                RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        }
    }
}

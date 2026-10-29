namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptBindingRegistry
    {
        private static readonly string[] BuiltInTypeNames =
        [
            "any", "unknown", "never", "number", "string", "boolean", "null", "void", "function", "array",
            "object",
        ];

        private readonly AsyncLocal<Func<object?, IReadOnlyList<object?>, CancellationToken, ValueTask<object?>>?>
            _invoker = new();

        public ValueTask<object?> InvokeCallableAsync(object? callable, IReadOnlyList<object?> arguments,
            CancellationToken token = default)
        {
            return (_invoker.Value ?? throw TypeError())(callable, arguments, token);
        }

        private static bool TypeComplexityAllowed(string name)
        {
            if (name.Length > 4096) return false;
            var nesting = 0;
            foreach (var character in name)
                if (character is '[' or '?' or '|' or '<' or '(' && ++nesting > 64)
                    return false;
            return true;
        }

        public static string UnwrapTypeGroup(string typeName)
        {
            typeName = typeName.Trim();
            while (typeName is ['(', .., ')'])
            {
                var depth = 0;
                var whole = true;
                for (var index = 0; index < typeName.Length - 1; index++)
                    if (typeName[index] == '(')
                    {
                        depth++;
                    }
                    else if (typeName[index] == ')' && --depth == 0)
                    {
                        whole = false;
                        break;
                    }

                if (!whole) break;
                typeName = typeName[1..^1].Trim();
            }

            return typeName;
        }

        private static bool TypeSyntaxAllowed(string text)
        {
            if (text.Length == 0) return false;
            if (char.IsLetter(text[0]) || text[0] is '_' or '$')
            {
                var simple = true;
                // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
                foreach (var character in text)
                    if (!char.IsLetterOrDigit(character) && character is not '_' and not '$')
                    {
                        simple = false;
                        break;
                    }

                if (simple) return text.Length <= 4096;
            }

            return ComplexTypeSyntaxAllowed(text);
        }

        private static bool ComplexTypeSyntaxAllowed(string text)
        {
            if (!TypeComplexityAllowed(text)) return false;
            var position = 0;
            return ReadUnion(0) && position == text.Length;

            void Space()
            {
                while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
            }

            bool Take(char character)
            {
                Space();
                if (position >= text.Length || text[position] != character) return false;
                position++;
                return true;
            }

            bool ReadUnion(int depth)
            {
                if (!ReadPostfix(depth)) return false;
                while (Take('|'))
                    if (!ReadPostfix(depth))
                        return false;
                Space();
                return true;
            }

            bool ReadPostfix(int depth)
            {
                if (depth > 64) return false;
                Space();
                if (Take('('))
                {
                    if (!ReadUnion(depth + 1) || !Take(')')) return false;
                }
                else
                {
                    if (position >= text.Length || !(char.IsLetter(text[position]) || text[position] is '_' or '$'))
                        return false;
                    position++;
                    while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] == '_'))
                        position++;
                    if (Take('<'))
                    {
                        if (!ReadUnion(depth + 1)) return false;
                        while (Take(','))
                            if (!ReadUnion(depth + 1))
                                return false;
                        if (!Take('>')) return false;
                    }
                }

                while (true)
                {
                    if (Take('?')) continue;
                    if (!Take('[')) return true;
                    if (!Take(']')) return false;
                }
            }
        }

        public IReadOnlyList<string> GetTypeNames()
        {
            return
            [
                .. BuiltInTypeNames
                    .Concat(_types.Keys).Concat(_tables.Keys).Concat(_schemas.Keys).Concat(_enums.Keys)
                    .Concat(_genericTypes.Keys)
                    .Distinct(StringComparer.Ordinal),
            ];
        }

        public bool IsKnownType(string typeName)
        {
            return IsKnownTypeCore(typeName, false);
        }

        private bool IsKnownTypeCore(string typeName, bool templates)
        {
            while (true)
            {
                if (templates && typeName is ['$', var digit] && char.IsAsciiDigit(digit)) return true;
                if (typeName is "any" or "unknown" or "never" or "number" or "string" or "boolean" or "null" or "void"
                    or "function" or "array" or "object")
                    return true;
                if (TryGetCachedGenericType(typeName, out var name, out var arguments))
                {
                    if (!_genericTypes.TryGetValue(name, out var generic) || generic.Arity != arguments.Length)
                        return false;
                    foreach (var argument in arguments)
                        if (!IsKnownTypeCore(argument, templates))
                            return false;
                    return true;
                }

                if (!TypeSyntaxAllowed(typeName)) return false;
                typeName = UnwrapTypeGroup(typeName);
                var alternatives = typeName.Contains('|') ? SplitType(typeName, '|') : [];
                if (alternatives.Length > 1)
                {
                    foreach (var alternative in alternatives)
                        if (!IsKnownTypeCore(alternative, templates))
                            return false;
                    return true;
                }

                if (typeName.EndsWith('?'))
                {
                    typeName = typeName[..^1];
                    continue;
                }

                if (typeName.EndsWith("[]", StringComparison.Ordinal))
                {
                    typeName = typeName[..^2];
                    continue;
                }

                return typeName is "any" or "unknown" or "never" or "number" or "string" or "boolean" or "null"
                           or "void"
                           or "function" or "array" or "object" ||
                       (templates && typeName.StartsWith('$') && int.TryParse(typeName.AsSpan(1), out var index) &&
                        index >= 0) ||
                       _types.ContainsKey(typeName) || _schemas.ContainsKey(typeName) || _enums.ContainsKey(typeName) ||
                       _genericTypes.ContainsKey(typeName) || _tables.ContainsKey(typeName);
            }
        }

        public bool IsAssignable(string actualType, string expectedType)
        {
            if (!IsKnownTypeCore(actualType, true) || !IsKnownTypeCore(expectedType, true)) return false;
            actualType = UnwrapTypeGroup(actualType);
            expectedType = UnwrapTypeGroup(expectedType);
            switch (actualType)
            {
                case "never" or "any" or "unknown":
                case "null" when expectedType == "void": return true;
            }

            if (expectedType is "any" or "unknown" ||
                actualType == expectedType) return true;
            var actualAlternatives = SplitType(actualType, '|');
            if (actualAlternatives.Length > 1) return actualAlternatives.All(t => IsAssignable(t, expectedType));
            if (actualType.EndsWith('?'))
                return IsAssignable("null", expectedType) && IsAssignable(actualType[..^1], expectedType);
            var expectedAlternatives = SplitType(expectedType, '|');
            if (expectedAlternatives.Length > 1) return expectedAlternatives.Any(t => IsAssignable(actualType, t));
            if (expectedType.EndsWith('?'))
                return actualType is "null" or "void" || IsAssignable(actualType, expectedType[..^1]);
            if (expectedType.EndsWith("[]", StringComparison.Ordinal))
                return actualType == "array" || (actualType.EndsWith("[]", StringComparison.Ordinal) &&
                                                 IsAssignable(actualType[..^2], expectedType[..^2]));
            if (expectedType == "array" && actualType.EndsWith("[]", StringComparison.Ordinal)) return true;
            if (TryGetCachedGenericType(actualType, out var actualName, out var actualArguments))
            {
                if (actualName == expectedType) return true;
                if (TryGetCachedGenericType(expectedType, out var expectedName, out var expectedArguments))
                    return actualName == expectedName && actualArguments.Length == expectedArguments.Length &&
                           actualArguments.Zip(expectedArguments).All(pair => IsAssignable(pair.First, pair.Second));
            }

            if (TryGetCachedGenericType(expectedType, out var expectedFamily, out _) &&
                actualType == expectedFamily) return true;
            return (actualType == "object" && _schemas.ContainsKey(expectedType)) ||
                   (actualType == "string" && _enums.ContainsKey(expectedType));
        }
    }
}

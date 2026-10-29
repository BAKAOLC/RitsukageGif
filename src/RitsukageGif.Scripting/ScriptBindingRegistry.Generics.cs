namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptBindingRegistry
    {
        private readonly Dictionary<string, (string Name, string[] Arguments)> _genericSyntax =
            new(StringComparer.Ordinal);

        private readonly Dictionary<string, ScriptGenericTypeBinding> _genericTypes = new(StringComparer.Ordinal);

        private readonly Func<object?, string, object?>?[] _nestedValidators = new Func<object?, string, object?>?[65];

        public static bool TryGetGenericType(string typeName, out string name, out string[] arguments)
        {
            name = "";
            arguments = [];
            if (!TypeSyntaxAllowed(typeName)) return false;
            typeName = UnwrapTypeGroup(typeName);
            var start = typeName.IndexOf('<');
            if (start <= 0 || !typeName.EndsWith('>')) return false;
            name = typeName[..start].Trim();
            if (!(char.IsLetter(name[0]) || name[0] == '_') ||
                name.Any(character => !char.IsLetterOrDigit(character) && character != '_'))
            {
                name = "";
                return false;
            }

            var nesting = 0;
            for (var index = start; index < typeName.Length; index++)
                switch (typeName[index])
                {
                    case '<':
                        nesting++;
                        break;
                    case '>' when --nesting < 0 || (nesting == 0 && index != typeName.Length - 1):
                        name = "";
                        return false;
                }

            if (nesting != 0)
            {
                name = "";
                return false;
            }

            var parts = SplitType(typeName[(start + 1)..^1], ',');
            if (parts.Any(string.IsNullOrWhiteSpace))
            {
                name = "";
                return false;
            }

            arguments = parts;
            return true;
        }

        private static string[] SplitType(string typeName, char separator)
        {
            List<string>? parts = null;
            var depth = 0;
            var start = 0;
            for (var index = 0; index < typeName.Length; index++)
            {
                switch (typeName[index])
                {
                    case '<' or '(' or '[':
                        depth++;
                        break;
                    case '>' or ')' or ']':
                        depth--;
                        break;
                    case var character when character == separator && depth == 0:
                        parts ??= [];
                        parts.Add(typeName[start..index].Trim());
                        start = index + 1;
                        break;
                }

                if (depth < 0) return [typeName];
            }

            if (parts is null || depth != 0) return [typeName];
            parts.Add(typeName[start..].Trim());
            return [.. parts];
        }

        private bool TryGetCachedGenericType(string typeName, out string name, out string[] arguments)
        {
            if (!typeName.Contains('<'))
            {
                name = "";
                arguments = [];
                return false;
            }

            lock (_genericSyntax)
            {
                if (_genericSyntax.TryGetValue(typeName, out var cached))
                {
                    name = cached.Name;
                    arguments = cached.Arguments;
                    return true;
                }

                if (!TryGetGenericType(typeName, out name, out arguments)) return false;
                if (_genericSyntax.Count >= 256) _genericSyntax.Clear();
                _genericSyntax.Add(typeName, (name, arguments));
                return true;
            }
        }

        // ReSharper disable once UnusedMethodReturnValue.Global
        public ScriptBindingRegistry RegisterGenericType(string name, ScriptGenericTypeBinding binding)
        {
            if (binding.Arity is <= 0 or > 64 ||
                (binding.OutcomeValueParameter is { } parameter && (parameter < 0 || parameter >= binding.Arity)))
                throw new ArgumentOutOfRangeException(nameof(binding));
            _genericTypes.Add(name, binding);
            return this;
        }

        public ScriptGenericTypeBinding? GetGenericTypeBinding(string name)
        {
            return _genericTypes.GetValueOrDefault(name);
        }

        private object? ValidateGeneric(object? value, string name, string[] arguments, int depth)
        {
            if (!_genericTypes.TryGetValue(name, out var binding) || binding.Arity != arguments.Length)
                throw TypeError();
            return ValidateGeneric(value, binding, arguments, depth);
        }

        private object? ValidateGeneric(object? value, ScriptGenericTypeBinding binding, string[] arguments, int depth)
        {
            var validator = _nestedValidators[depth];
            if (validator is null)
            {
                Func<object?, string, object?> candidate = (item, type) => ValidateCore(item, type, depth + 1);
                validator = Interlocked.CompareExchange(ref _nestedValidators[depth], candidate, null) ?? candidate;
            }

            return binding.Validate(value, arguments, validator);
        }

        private IReadOnlyList<ScriptMemberDescriptor> GetGenericMembers(string name, string[] arguments)
        {
            if (!_genericTypes.TryGetValue(name, out var binding) || binding.Arity != arguments.Length) return [];
            var members = GetMembers(name);
            return binding.SpecializeMembers?.Invoke(members, arguments) ?? members;
        }
    }
}

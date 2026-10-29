namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptBindingRegistry
    {
        private readonly Func<object?, string, bool>?[] _nestedMatchers = new Func<object?, string, bool>?[65];
        private readonly Dictionary<string, IReadOnlyList<string>> _patternCases = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ScriptPatternDescriptor> _patterns = new(StringComparer.Ordinal);

        // ReSharper disable once UnusedMethodReturnValue.Global
        public ScriptBindingRegistry RegisterPattern(ScriptPatternDescriptor descriptor)
        {
            if (string.IsNullOrWhiteSpace(descriptor.Name))
                throw new ArgumentException(UiText.Get("ScriptTypeError"), nameof(descriptor));
            _patterns.Add(descriptor.Name,
                descriptor with { PayloadTypes = Array.AsReadOnly([.. descriptor.PayloadTypes]) });
            return this;
        }

        public ScriptPatternDescriptor? GetPattern(string name)
        {
            return _patterns.GetValueOrDefault(name);
        }

        public IReadOnlyList<ScriptPatternDescriptor> GetPatterns()
        {
            return [.. _patterns.Values];
        }

        // ReSharper disable once UnusedMethodReturnValue.Global
        public ScriptBindingRegistry RegisterPatternCases(string typeName, IReadOnlyList<string> cases)
        {
            if (string.IsNullOrWhiteSpace(typeName) || cases.Count == 0 ||
                cases.Distinct(StringComparer.Ordinal).Count() != cases.Count ||
                cases.Any(name => !_patterns.TryGetValue(name, out var pattern) || pattern.InputType != typeName))
                throw new ArgumentException(UiText.Get("ScriptTypeError"), nameof(cases));
            _patternCases.Add(typeName, Array.AsReadOnly([.. cases]));
            return this;
        }

        public IReadOnlyList<string>? GetPatternCases(string typeName)
        {
            return _patternCases.GetValueOrDefault(typeName);
        }

        public bool TryReadPatternProperty(object? instance, string name, out object? value)
        {
            switch (instance)
            {
                case Dictionary<string, object?> fields: return fields.TryGetValue(name, out value);
                case ScriptTableBinding table when table.Contains(name):
                    value = table.Get(name);
                    return true;
            }

            var host = FindHost(instance);
            if (host is not null) return host.TryReadProperty(instance!, name, out value);

            value = null;
            return false;
        }

        public bool TryMatchPattern(string name, object? value, out IReadOnlyList<object?> payload)
        {
            var descriptor = GetPattern(name) ??
                             throw new InvalidOperationException(UiText.Format("DslUnknownName", name));
            if (!descriptor.TryMatch(value, out payload))
            {
                payload = [];
                return false;
            }

            if (payload.Count != descriptor.PayloadTypes.Count) throw TypeError();
            return true;
        }

        public bool IsInstanceOf(object? value, string typeName)
        {
            return IsKnownType(typeName) && IsInstanceOfCore(value, typeName, 0);
        }

        private bool IsInstanceOfCore(object? value, string typeName, int depth)
        {
            if (depth > 64) return false;
            typeName = UnwrapTypeGroup(typeName);
            if (typeName.Contains('|'))
            {
                var alternatives = SplitType(typeName, '|');
                if (alternatives.Length > 1) return alternatives.Any(type => IsInstanceOfCore(value, type, depth + 1));
            }

            if (typeName.EndsWith('?')) return value is null || IsInstanceOfCore(value, typeName[..^1], depth + 1);
            switch (typeName)
            {
                case "any" or "unknown": return true;
                case "never": return false;
                case "null" or "void": return value is null;
                case "number":
                    return value switch
                    {
                        double number => double.IsFinite(number), float number => float.IsFinite(number),
                        decimal or byte or sbyte or short or ushort or int or uint or long or ulong => true,
                        _ => false,
                    };
                case "string": return value is string;
                case "boolean": return value is bool;
                case "function": return value is IScriptCallable or ScriptNative;
                case "array": return value is List<object?>;
                case "object": return value is Dictionary<string, object?>;
            }

            if (typeName.EndsWith("[]", StringComparison.Ordinal))
            {
                if (value is not List<object?> array) return false;
                var elementType = typeName[..^2];
                // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
                foreach (var item in array)
                    if (!IsInstanceOfCore(item, elementType, depth + 1))
                        return false;
                return true;
            }

            if (TryGetCachedGenericType(typeName, out var name, out var arguments))
            {
                if (!_genericTypes.TryGetValue(name, out var generic) || generic.Arity != arguments.Length ||
                    generic.Matches is null) return false;
                var matcher = _nestedMatchers[depth];
                if (matcher is null)
                {
                    Func<object?, string, bool> candidate = (item, type) => IsInstanceOfCore(item, type, depth + 1);
                    matcher = Interlocked.CompareExchange(ref _nestedMatchers[depth], candidate, null) ?? candidate;
                }

                return generic.Matches(value, arguments, matcher);
            }

            if (_types.TryGetValue(typeName, out var host)) return value is not null && host.Matches(value);
            if (_enums.TryGetValue(typeName, out var enumeration))
                return enumeration.Values.Values.Any(item => Equals(item, value));
            if (_schemas.TryGetValue(typeName, out var schema))
            {
                if (value is not Dictionary<string, object?> fields) return false;
                foreach (var property in schema.Properties)
                    if (!fields.TryGetValue(property.Name, out var field))
                    {
                        if (!property.Optional) return false;
                    }
                    else if (!IsInstanceOfCore(field, property.TypeName, depth + 1))
                    {
                        return false;
                    }

                return schema.AllowExtra ||
                       fields.Keys.All(key => schema.Properties.Any(property => property.Name == key));
            }

            return value is ScriptTableBinding table && table.Name == typeName;
        }
    }
}

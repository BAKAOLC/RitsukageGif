using System.Collections.ObjectModel;

namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptBindingRegistry
    {
        private sealed record Global(object? Value, ScriptMemberDescriptor Descriptor);

        private sealed record ObjectSchema(IReadOnlyList<ScriptPropertyDescriptor> Properties, bool AllowExtra);

        private sealed record EnumBinding(
            Type ValueType,
            Dictionary<string, object?> Values,
            ScriptEnumDescriptor Descriptor);

        private readonly Dictionary<string, string> _declarations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Global> _globals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ScriptTableBinding> _tables = new(StringComparer.Ordinal);
        private readonly List<IScriptHostBinding> _hostBindings = [];
        private readonly Dictionary<string, IScriptHostBinding> _types = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ObjectSchema> _schemas = new(StringComparer.Ordinal);
        private readonly Dictionary<string, EnumBinding> _enums = new(StringComparer.Ordinal);

        public IReadOnlyList<ScriptMemberDescriptor> GetGlobals() =>
            _globals.Values.Select(g => g.Descriptor).ToArray();

        public IReadOnlyList<ScriptMemberDescriptor> GetMembers(string typeName)
        {
            typeName = UnwrapTypeGroup(typeName);
            if (TryGetCachedGenericType(typeName, out var genericName, out var genericArguments))
                return GetGenericMembers(genericName, genericArguments);
            if (_enums.TryGetValue(typeName, out var enumeration))
                return enumeration.Values.Keys.Select(name => new ScriptMemberDescriptor(name, "enumMember", typeName))
                    .ToArray();
            if (_tables.TryGetValue(typeName, out var table)) return table.Members;
            if (_types.TryGetValue(typeName, out var type)) return type.Members;
            if (_schemas.TryGetValue(typeName, out var schema))
                return schema.Properties
                    .Select(p => new ScriptMemberDescriptor(p.Name, "property", p.TypeName, Writable: true)).ToArray();
            return [];
        }

        public ScriptBindingRegistry RegisterDeclarationSchema(string blockName, string typeName)
        {
            _declarations.Add(blockName, typeName);
            return this;
        }

        public string? GetDeclarationSchema(string blockName) => _declarations.GetValueOrDefault(blockName);

        public IReadOnlyList<ScriptPropertyDescriptor>? GetObjectSchema(string name) =>
            _schemas.GetValueOrDefault(name)?.Properties;

        public ScriptEnumDescriptor? GetEnum(string name) => _enums.GetValueOrDefault(name)?.Descriptor;

        public ScriptBindingRegistry RegisterFunction(string name, ScriptNative callback,
            IReadOnlyList<ScriptParameter>? parameters = null, string returnType = "any", string? description = null,
            string? inferredReturnType = null, bool invalidatesReceiverElement = false)
        {
            var function = CreateFunction(name, callback, parameters, returnType, description, inferredReturnType,
                invalidatesReceiverElement);
            _globals.Add(name, new Global(function, new ScriptMemberDescriptor(name, "function", "function",
                function.Descriptor, description)));
            return this;
        }

        public ScriptBindingRegistry RegisterValue(string name, object? value, string typeName = "any",
            string? description = null)
        {
            _globals.Add(name, new Global(Validate(value, typeName),
                new ScriptMemberDescriptor(name, "variable", typeName, Description: description)));
            return this;
        }

        public ScriptTableBinding RegisterTable(string name, string? description = null)
        {
            var table = new ScriptTableBinding(this, name);
            _tables.Add(name, table);
            _globals.Add(name,
                new Global(table, new ScriptMemberDescriptor(name, "module", name, Description: description)));
            return table;
        }

        public ScriptBindingRegistry RegisterEnum<T>(string name, IReadOnlyDictionary<string, T> values,
            bool exposeGlobal = true)
        {
            if (!_enums.TryGetValue(name, out var binding))
            {
                var entries = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                binding = new EnumBinding(typeof(T), entries, new ScriptEnumDescriptor(name,
                    new ReadOnlyDictionary<string, object?>(entries)));
                _enums.Add(name, binding);
            }
            else if (binding.ValueType != typeof(T)) throw TypeError();

            var table = _tables.GetValueOrDefault(name);
            if (exposeGlobal)
            {
                if (!_tables.TryGetValue(name, out table)) table = RegisterTable(name);
                _globals[name] = new Global(table, new ScriptMemberDescriptor(name, "enum", name));
            }

            foreach (var pair in values)
            {
                if (binding.Values.TryGetValue(pair.Key, out var existing))
                {
                    if (!Equals(existing, pair.Value))
                        throw new InvalidOperationException(UiText.Format("DslDuplicate", pair.Key));
                    continue;
                }

                binding.Values.Add(pair.Key, pair.Value);
            }

            if (table is not null)
                foreach (var pair in binding.Values)
                    if (!table.Contains(pair.Key))
                        table.Value(pair.Key, pair.Value, name);
            return this;
        }

        public ScriptBindingRegistry ExtendEnum<T>(string name, IReadOnlyDictionary<string, T> values) =>
            RegisterEnum(name, values);

        public ScriptBindingRegistry RegisterObjectSchema(string name,
            IReadOnlyList<ScriptPropertyDescriptor> properties,
            bool allowExtra = false)
        {
            if (properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != properties.Count)
                throw TypeError();
            _schemas.Add(name, new ObjectSchema(properties.ToArray(), allowExtra));
            return this;
        }

        public ScriptHostType<T> RegisterType<T>(string name)
        {
            var binding = new ScriptHostType<T>(this, name);
            _types.Add(name, binding);
            _hostBindings.Add(binding);
            return binding;
        }

        public object? Validate(object? value, string typeName)
        {
            switch (typeName)
            {
                case "any":
                case "unknown": return value;
                case "number": return ValidateNumber(value);
                case "string": return value is string ? value : throw TypeError();
                case "boolean": return value is bool ? value : throw TypeError();
                case "null":
                case "void": return value is null ? null : throw TypeError();
                case "function": return value is IScriptCallable or ScriptNative ? value : throw TypeError();
                case "array": return value is List<object?> ? value : throw TypeError();
                case "object": return value is Dictionary<string, object?> ? value : throw TypeError();
                case "never": throw TypeError();
            }

            ScriptGenericTypeBinding? generic = null;
            if (TryGetCachedGenericType(typeName, out var genericName, out var genericArguments))
            {
                if (!_genericTypes.TryGetValue(genericName, out generic) || generic.Arity != genericArguments.Length)
                    throw TypeError();
                foreach (var argument in genericArguments)
                    if (!IsKnownTypeCore(argument, true))
                        throw TypeError();
            }
            else
            {
                if (!TypeComplexityAllowed(typeName))
                    throw new InvalidOperationException(UiText.Get("DslExecutionLimit"));
                if (!IsKnownTypeCore(typeName, true)) throw TypeError();
            }

            var transaction = BeginValidation(out var ownsTransaction, out var checkpoint);
            var success = false;
            try
            {
                var result = generic is null
                    ? ValidateCore(value, typeName, 0)
                    : ValidateGeneric(value, generic, genericArguments, 0);
                success = true;
                return result;
            }
            finally
            {
                EndValidation(transaction, ownsTransaction, checkpoint, success);
            }
        }

        // Bound recursive schema validation even for cyclic values supplied by a host.
        // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
        private object? ValidateCore(object? value, string typeName, int depth)
        {
            if (depth > 64) throw new InvalidOperationException(UiText.Get("DslExecutionLimit"));
            if (typeName.Length == 2 && typeName[0] == '$' && char.IsAsciiDigit(typeName[1])) return value;
            typeName = UnwrapTypeGroup(typeName);
            var alternatives = typeName.Contains('|') ? SplitType(typeName, '|') : [];
            if (alternatives.Length > 1)
            {
                foreach (var option in alternatives)
                {
                    var checkpoint = _validationTransaction!.Pending.Count;
                    try
                    {
                        return ValidateCore(value, option, depth + 1);
                    }
                    catch (InvalidOperationException)
                    {
                        var pending = _validationTransaction.Pending;
                        pending.RemoveRange(checkpoint, pending.Count - checkpoint);
                    }
                }

                throw TypeError();
            }

            if (typeName.EndsWith('?'))
            {
                if (value is null) return null;
                return ValidateCore(value, typeName[..^1], depth + 1);
            }

            if (typeName is "any" or "unknown" ||
                typeName.StartsWith('$') && int.TryParse(typeName.AsSpan(1), out _)) return value;
            if (typeName.EndsWith("[]", StringComparison.Ordinal))
            {
                if (value is not List<object?> array) throw TypeError();
                List<object?>? normalized = null;
                var elementType = typeName[..^2];
                for (var index = 0; index < array.Count; index++)
                {
                    var item = ValidateCore(array[index], elementType, depth + 1);
                    if (Equals(item, array[index])) continue;
                    normalized ??= new List<object?>(array);
                    normalized[index] = item;
                }

                var result = normalized ?? array;
                StageArrayContract(array, result, elementType);
                return result;
            }

            if (TryGetCachedGenericType(typeName, out var genericName, out var genericArguments))
                return ValidateGeneric(value, genericName, genericArguments, depth);
            if (typeName is "void" or "null") return value is null ? null : throw TypeError();
            if (typeName == "function") return value is IScriptCallable or ScriptNative ? value : throw TypeError();
            if (typeName == "number") return ValidateNumber(value);

            if (typeName == "string") return value is string ? value : throw TypeError();
            if (typeName == "boolean") return value is bool ? value : throw TypeError();
            if (typeName == "array") return value is List<object?> ? value : throw TypeError();
            if (typeName == "object") return value is Dictionary<string, object?> ? value : throw TypeError();
            if (_enums.TryGetValue(typeName, out var enumeration))
            {
                if (value is string name)
                {
                    if (name.StartsWith(typeName + ".", StringComparison.Ordinal)) name = name[(typeName.Length + 1)..];
                    if (enumeration.Values.TryGetValue(name, out var found)) return found;
                }

                return enumeration.Values.Values.Any(entry => Equals(entry, value)) ? value : throw TypeError();
            }

            if (_schemas.TryGetValue(typeName, out var schema))
            {
                if (value is not Dictionary<string, object?> source) throw TypeError();
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var property in schema.Properties)
                {
                    if (source.TryGetValue(property.Name, out var field))
                        result[property.Name] = ValidateCore(field, property.TypeName, depth + 1);
                    else if (!property.Optional)
                        throw new InvalidOperationException(UiText.Format("DslArguments", property.Name));
                    else if (property.DefaultValue is not null)
                        result[property.Name] = ValidateCore(property.DefaultValue, property.TypeName, depth + 1);
                }

                foreach (var field in source)
                {
                    if (schema.Properties.Any(p => p.Name == field.Key)) continue;
                    if (!schema.AllowExtra)
                        throw new InvalidOperationException(UiText.Format("DslUnknownName", field.Key));
                    result.Add(field.Key, field.Value);
                }

                return result;
            }

            if (_types.TryGetValue(typeName, out var host) && value is not null && host.Matches(value)) return value;
            if (value is ScriptTableBinding table && table.Name == typeName) return value;
            throw TypeError();
        }

        private static object ValidateNumber(object? value)
        {
            if (value is double existing) return double.IsFinite(existing) ? value : throw TypeError();
            var number = value switch
            {
                float f => f, decimal d => (double)d, byte b => b, sbyte b => b,
                short n => n, ushort n => n, int n => n, uint n => n, long n => n, ulong n => n,
                _ => throw TypeError()
            };
            return double.IsFinite(number) ? number : throw TypeError();
        }

        internal IEnumerable<KeyValuePair<string, object?>> BindGlobals() => _globals.Select(p =>
            new KeyValuePair<string, object?>(p.Key, p.Value.Value));

        internal ScriptCallableDescriptor CreateDescriptor(string name, IReadOnlyList<ScriptParameter>? parameters,
            string returnType, string? description, string? inferredReturnType = null,
            bool invalidatesReceiverElement = false)
        {
            var entries = parameters?.Select(parameter => parameter.CallbackParameters is null
                    ? parameter
                    : parameter with { CallbackParameters = Array.AsReadOnly(parameter.CallbackParameters.ToArray()) })
                .ToArray() ?? [];
            if (entries.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != entries.Length)
                throw TypeError();
            var optional = false;
            for (var i = 0; i < entries.Length; i++)
            {
                var parameter = entries[i];
                if (parameter.Variadic && i != entries.Length - 1 ||
                    optional && !parameter.Optional && !parameter.Variadic)
                    throw TypeError();
                optional |= parameter.Optional;
            }

            return new ScriptCallableDescriptor(name, entries, returnType, description, inferredReturnType,
                invalidatesReceiverElement);
        }

        internal ScriptBoundFunction CreateFunction(string name, ScriptNative callback,
            IReadOnlyList<ScriptParameter>? parameters, string returnType, string? description,
            string? inferredReturnType = null, bool invalidatesReceiverElement = false) =>
            new(
                CreateDescriptor(name, parameters, returnType, description, inferredReturnType,
                    invalidatesReceiverElement), callback);

        internal async ValueTask<object?> InvokeAsync(ScriptBoundFunction function, IReadOnlyList<object?> arguments,
            CancellationToken token,
            Func<object?, IReadOnlyList<object?>, CancellationToken, ValueTask<object?>> invoker)
        {
            token.ThrowIfCancellationRequested();
            var parameters = function.Descriptor.Parameters;
            var normalized = new List<object?>();
            var index = 0;
            foreach (var parameter in parameters)
            {
                if (parameter.Variadic)
                {
                    while (index < arguments.Count) normalized.Add(Validate(arguments[index++], parameter.TypeName));
                    break;
                }

                if (index < arguments.Count) normalized.Add(Validate(arguments[index++], parameter.TypeName));
                else if (parameter.Optional)
                    normalized.Add(parameter.DefaultValue is null
                        ? null
                        : Validate(parameter.DefaultValue, parameter.TypeName));
                else throw new InvalidOperationException(UiText.Format("DslArguments", function.Descriptor.Name));
            }

            if (index != arguments.Count)
                throw new InvalidOperationException(UiText.Format("DslArguments", function.Descriptor.Name));
            var previous = _invoker.Value;
            _invoker.Value = invoker;
            try
            {
                var result = await function.Callback(normalized, token);
                token.ThrowIfCancellationRequested();
                return Validate(result, function.Descriptor.ReturnTypeName);
            }
            finally
            {
                _invoker.Value = previous;
            }
        }

        internal object? GetMember(object? instance, object? key)
        {
            if (instance is ScriptTableBinding table && key is string name) return table.Get(name);
            var binding = FindHost(instance);
            return binding is null ? throw TypeError() : binding.Get(instance!, key);
        }

        internal void SetMember(object? instance, object? key, object? value)
        {
            if (instance is ScriptTableBinding table && key is string name)
            {
                table.Set(name, value);
                return;
            }

            var binding = FindHost(instance);
            if (binding is null) throw TypeError();
            binding.Set(instance!, key, value);
        }

        private IScriptHostBinding? FindHost(object? value)
        {
            if (value is null) return null;
            for (var i = _hostBindings.Count - 1; i >= 0; i--)
                if (_hostBindings[i].Matches(value))
                    return _hostBindings[i];
            return null;
        }

        private static Exception TypeError() => new InvalidOperationException(UiText.Get("ScriptTypeError"));
    }
}

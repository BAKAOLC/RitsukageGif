namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptEngine
    {
        private sealed class Cell(object? value, bool readOnly, string? typeName)
        {
            public readonly bool ReadOnly = readOnly;
            public readonly string? TypeName = typeName;
            public Func<object?>? Getter;
            public object? Value = value;

            public object? Read()
            {
                return Getter is null ? Value : Getter();
            }
        }

        private sealed class Scope
        {
            private readonly ScriptBindingRegistry _bindings;
            private readonly Scope _executionScope;
            private readonly Scope? _parent;
            private readonly string? _returnType;
            private PropagationContext? _propagation;

            private Dictionary<string, Cell>? _values;

            public Scope(Scope? parent = null, ScriptBindingRegistry? bindings = null, bool functionScope = false,
                string? returnType = null)
            {
                _parent = parent;
                _bindings = bindings ?? parent!._bindings;
                _executionScope = functionScope || parent is null ? this : parent._executionScope;
                _returnType = returnType;
            }

            public bool HasPropagation => _executionScope._propagation?.HasPropagation ?? false;

            private Dictionary<string, Cell> Values => _values ??= new(StringComparer.Ordinal);

            public Evaluation Unwrap(object? value)
            {
                return (_executionScope._propagation ??= new(_executionScope._returnType))
                    .Unwrap(value);
            }

            public void ValidatePropagation(object? value)
            {
                _executionScope._propagation?.Validate(value);
            }

            public void Define(string name, object? value, bool readOnly = false, string? typeName = null)
            {
                if (typeName is not null) value = _bindings.Validate(value, typeName);
                DefineValidated(name, value, readOnly, typeName);
            }

            public void DefineValidated(string name, object? value, bool readOnly, string? typeName)
            {
                if (!Values.TryAdd(name, new(value, readOnly, typeName)))
                    throw new InvalidOperationException(UiText.Format("DslDuplicate", name));
            }

            public void DefineCell(string name, Cell cell)
            {
                if (!Values.TryAdd(name, cell))
                    throw new InvalidOperationException(UiText.Format("DslDuplicate", name));
            }

            private Cell Find(string name)
            {
                return _values?.GetValueOrDefault(name) ?? _parent?.Find(name) ??
                    throw new InvalidOperationException(UiText.Format("DslUnknownName", name));
            }

            public object? Get(string name)
            {
                return Find(name).Read();
            }

            public object? GetLocal(string name)
            {
                return _values is not null && _values.TryGetValue(name, out var cell)
                    ? cell.Read()
                    : throw new InvalidOperationException(UiText.Format("DslUnknownName", name));
            }

            public bool ContainsLocal(string name)
            {
                return _values?.ContainsKey(name) ?? false;
            }

            public void DefineImport(string name, Func<object?> getter)
            {
                if (!Values.TryAdd(name, new(null, true, null) { Getter = getter }))
                    throw new InvalidOperationException(UiText.Format("DslDuplicate", name));
            }

            public void Set(string name, object? value)
            {
                var cell = Find(name);
                if (cell.ReadOnly) throw new InvalidOperationException(UiText.Format("ScriptReadOnly", name));
                cell.Value = cell.TypeName is null ? value : _bindings.Validate(value, cell.TypeName);
            }

            public Scope Clone()
            {
                var copy = new Scope(_parent, _bindings);
                if (_values is null) return copy;
                foreach (var pair in _values)
                    copy.Values.Add(pair.Key,
                        new(pair.Value.Value, pair.Value.ReadOnly, pair.Value.TypeName)
                            { Getter = pair.Value.Getter });
                return copy;
            }

            public Dictionary<string, object?> Snapshot()
            {
                var result = _parent?.Snapshot() ?? new Dictionary<string, object?>();
                if (_values is not null)
                    foreach (var pair in _values)
                        result[pair.Key] = pair.Value.Read();
                return result;
            }
        }
    }
}

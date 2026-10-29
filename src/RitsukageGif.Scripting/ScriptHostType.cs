namespace RitsukageGif.Scripting
{
    public sealed class ScriptHostType<T> : IScriptHostBinding
    {
        private readonly Dictionary<string, Func<T, object?>> _getters = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ScriptMemberDescriptor> _members = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ScriptPropertyReader<T>> _propertyReaders = new(StringComparer.Ordinal);
        private readonly ScriptBindingRegistry _registry;
        private readonly Dictionary<string, Action<T, object?>> _setters = new(StringComparer.Ordinal);
        private Func<T, object?, bool>? _indexContains;
        private Func<T, object?, object?>? _indexGetter;
        private Action<T, object?, object?>? _indexSetter;
        private bool _preferIndexSetter;

        internal ScriptHostType(ScriptBindingRegistry registry, string name)
        {
            _registry = registry;
            Name = name;
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public string Name { get; }
        public IReadOnlyList<ScriptMemberDescriptor> Members => [.. _members.Values];

        bool IScriptHostBinding.Matches(object value)
        {
            return value is T;
        }

        bool IScriptHostBinding.TryReadProperty(object instance, string name, out object? value)
        {
            value = null;
            if (!_members.TryGetValue(name, out var member) || member.Kind != "property") return false;
            if (_propertyReaders.TryGetValue(name, out var reader))
            {
                if (!reader((T)instance, out var result)) return false;
                value = _registry.Validate(result, member.TypeName);
            }
            else
            {
                value = ((IScriptHostBinding)this).Get(instance, name);
            }

            return true;
        }

        object? IScriptHostBinding.Get(object instance, object? key)
        {
            if (_indexGetter is not null && _indexContains?.Invoke((T)instance, key) == true)
                return _indexGetter((T)instance, key);
            if (key is string name && _getters.TryGetValue(name, out var getter)) return getter((T)instance);
            if (_indexGetter is not null) return _indexGetter((T)instance, key);
            throw new InvalidOperationException(UiText.Get("ScriptTypeError"));
        }

        void IScriptHostBinding.Set(object instance, object? key, object? value)
        {
            if (_preferIndexSetter && _indexSetter is not null)
            {
                _indexSetter((T)instance, key, value);
                return;
            }

            if (key is string name && _members.ContainsKey(name))
            {
                if (!_setters.TryGetValue(name, out var setter))
                    throw new InvalidOperationException(UiText.Format("ScriptReadOnly", name));
                setter((T)instance, value);
                return;
            }

            if (_indexSetter is not null)
            {
                _indexSetter((T)instance, key, value);
                return;
            }

            throw new InvalidOperationException(UiText.Get("ScriptTypeError"));
        }

        // ReSharper disable once UnusedMethodReturnValue.Global
        public ScriptHostType<T> Property(string name, string typeName, Func<T, object?> getter,
            Action<T, object?>? setter = null, string? description = null, ScriptPropertyReader<T>? tryRead = null)
        {
            _members.Add(name, new(name, "property", typeName,
                Description: description, Writable: setter is not null));
            _getters.Add(name, target => _registry.Validate(getter(target), typeName));
            if (tryRead is not null) _propertyReaders.Add(name, tryRead);
            if (setter is not null)
                _setters.Add(name, (target, value) => setter(target, _registry.Validate(value, typeName)));
            return this;
        }

        // ReSharper disable once UnusedMethodReturnValue.Global
        public ScriptHostType<T> Method(string name, ScriptHostMethod<T> callback,
            IReadOnlyList<ScriptParameter>? parameters = null, string returnType = "any", string? description = null,
            string? inferredReturnType = null, bool invalidatesReceiverElement = false)
        {
            var descriptor = ScriptBindingRegistry.CreateDescriptor(Name + "." + name, parameters, returnType,
                description,
                inferredReturnType, invalidatesReceiverElement);
            _members.Add(name, new(name, "method", "function", descriptor, description));
            _getters.Add(name,
                target => new ScriptBoundFunction(descriptor, (args, token) => callback(target, args, token)));
            return this;
        }

        // ReSharper disable once UnusedMethodReturnValue.Global
        public ScriptHostType<T> Indexer(Func<T, object?, object?> getter, Action<T, object?, object?>? setter = null,
            Func<T, object?, bool>? contains = null, bool preferSetter = false)
        {
            _indexGetter = getter;
            _indexContains = contains;
            _preferIndexSetter = preferSetter;
            _indexSetter = setter;
            return this;
        }
    }
}

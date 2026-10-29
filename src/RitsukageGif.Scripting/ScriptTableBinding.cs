namespace RitsukageGif.Scripting
{
    public sealed class ScriptTableBinding
    {
        private readonly Dictionary<string, ScriptMemberDescriptor> _members = new(StringComparer.Ordinal);
        private readonly ScriptBindingRegistry _registry;
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

        internal ScriptTableBinding(ScriptBindingRegistry registry, string name)
        {
            _registry = registry;
            Name = name;
        }

        public string Name { get; }
        public IReadOnlyList<ScriptMemberDescriptor> Members => [.. _members.Values];

        // ReSharper disable once UnusedMethodReturnValue.Global
        public ScriptTableBinding Function(string name, ScriptNative callback,
            IReadOnlyList<ScriptParameter>? parameters = null, string returnType = "any", string? description = null,
            string? inferredReturnType = null, bool invalidatesReceiverElement = false)
        {
            var callable = ScriptBindingRegistry.CreateFunction(Name + "." + name, callback, parameters, returnType,
                description,
                inferredReturnType, invalidatesReceiverElement);
            _values.Add(name, callable);
            _members.Add(name,
                new(name, "method", "function", callable.Descriptor, description));
            return this;
        }

        public ScriptTableBinding Value(string name, object? value, string typeName = "any",
            string? description = null, bool writable = false)
        {
            _values.Add(name, _registry.Validate(value, typeName));
            _members.Add(name, new(name, "property", typeName, Description: description,
                Writable: writable));
            return this;
        }

        internal bool Contains(string name)
        {
            return _values.ContainsKey(name);
        }

        internal object? Get(string name)
        {
            return _values.TryGetValue(name, out var value)
                ? value
                : throw new InvalidOperationException(UiText.Format("DslUnknownName", name));
        }

        internal void Set(string name, object? value)
        {
            if (!_members.TryGetValue(name, out var member))
                throw new InvalidOperationException(UiText.Format("DslUnknownName", name));
            if (!member.Writable) throw new InvalidOperationException(UiText.Format("ScriptReadOnly", name));
            _values[name] = _registry.Validate(value, member.TypeName);
        }
    }
}

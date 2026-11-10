namespace RitsukageGif.Scripting
{
    internal sealed class ScriptMetadataHostBinding(IReadOnlyList<ScriptMemberDescriptor> members) : IScriptHostBinding
    {
        public IReadOnlyList<ScriptMemberDescriptor> Members { get; } = members;

        public bool Matches(object value)
        {
            return false;
        }

        public object Get(object instance, object? key)
        {
            throw Unavailable();
        }

        public bool TryReadProperty(object instance, string name, out object? value)
        {
            value = null;
            throw Unavailable();
        }

        public void Set(object instance, object? key, object? value)
        {
            throw Unavailable();
        }

        private static InvalidOperationException Unavailable()
        {
            return new("Script metadata bindings cannot execute operations.");
        }
    }
}

namespace RitsukageGif.Scripting
{
    internal interface IScriptHostBinding
    {
        IReadOnlyList<ScriptMemberDescriptor> Members { get; }
        bool Matches(object value);
        object? Get(object instance, object? key);
        bool TryReadProperty(object instance, string name, out object? value);
        void Set(object instance, object? key, object? value);
    }
}

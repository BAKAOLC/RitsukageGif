namespace RitsukageGif.Scripting
{
    internal interface IScriptHostBinding
    {
        string Name { get; }
        bool Matches(object value);
        IReadOnlyList<ScriptMemberDescriptor> Members { get; }
        object? Get(object instance, object? key);
        bool TryReadProperty(object instance, string name, out object? value);
        void Set(object instance, object? key, object? value);
    }
}

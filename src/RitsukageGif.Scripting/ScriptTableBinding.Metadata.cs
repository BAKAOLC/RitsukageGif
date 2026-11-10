namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptTableBinding
    {
        internal void ImportMetadataMember(ScriptMemberDescriptor descriptor, object? value)
        {
            _members.Add(descriptor.Name, descriptor);
            _values.Add(descriptor.Name, descriptor.Callable is { } callable
                ? new ScriptBoundFunction(callable, ScriptBindingRegistry.MetadataUnavailable)
                : value);
        }
    }
}

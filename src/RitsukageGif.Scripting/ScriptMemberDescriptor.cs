namespace RitsukageGif.Scripting
{
    public sealed record ScriptMemberDescriptor(
        string Name,
        string Kind,
        string TypeName,
        ScriptCallableDescriptor? Callable = null,
        string? Description = null,
        bool Writable = false);
}

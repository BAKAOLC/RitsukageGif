namespace RitsukageGif.Scripting
{
    public sealed record ScriptMetadataMember(
        string Name,
        string Kind,
        string TypeName,
        ScriptMetadataCallable? Callable,
        string? Description,
        bool Writable);
}

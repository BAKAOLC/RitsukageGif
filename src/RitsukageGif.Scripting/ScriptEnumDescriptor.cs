namespace RitsukageGif.Scripting
{
    public sealed record ScriptEnumDescriptor(string Name, IReadOnlyDictionary<string, object?> Values);
}

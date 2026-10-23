namespace RitsukageGif.Scripting
{
    public sealed record ScriptPropertyDescriptor(
        string Name,
        string TypeName = "any",
        bool Optional = false,
        object? DefaultValue = null);
}

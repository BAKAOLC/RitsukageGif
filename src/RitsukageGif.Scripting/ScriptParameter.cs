namespace RitsukageGif.Scripting
{
    public sealed record ScriptParameter(
        string Name,
        string TypeName = "any",
        bool Optional = false,
        object? DefaultValue = null,
        bool Variadic = false,
        bool ReceiverElement = false,
        IReadOnlyList<string>? CallbackParameters = null,
        string? CallbackReturnType = null);
}

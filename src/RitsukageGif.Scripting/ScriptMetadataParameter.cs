namespace RitsukageGif.Scripting
{
    public sealed record ScriptMetadataParameter(
        string Name,
        string TypeName,
        bool Optional,
        ScriptMetadataValue? DefaultValue,
        bool Variadic,
        bool ReceiverElement,
        string[]? CallbackParameters,
        string? CallbackReturnType,
        int? MaximumCount);
}

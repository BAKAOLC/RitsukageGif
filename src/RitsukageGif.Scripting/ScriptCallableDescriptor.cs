namespace RitsukageGif.Scripting
{
    public sealed record ScriptCallableDescriptor(
        string Name,
        IReadOnlyList<ScriptParameter> Parameters,
        string ReturnTypeName = "any",
        string? Description = null,
        string? InferredReturnTypeName = null,
        bool InvalidatesReceiverElement = false);
}

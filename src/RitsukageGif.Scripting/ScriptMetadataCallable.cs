namespace RitsukageGif.Scripting
{
    public sealed record ScriptMetadataCallable(
        string Name,
        ScriptMetadataParameter[] Parameters,
        string ReturnTypeName,
        string? Description,
        string? InferredReturnTypeName,
        bool InvalidatesReceiverElement);
}

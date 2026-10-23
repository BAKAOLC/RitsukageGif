namespace RitsukageGif.Scripting
{
    public sealed record ScriptSignature(
        string Label,
        IReadOnlyList<string> Parameters,
        int ActiveParameter,
        string? Documentation = null);
}

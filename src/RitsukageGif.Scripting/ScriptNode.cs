namespace RitsukageGif.Scripting
{
    public sealed record ScriptNode(
        string Kind,
        string Text,
        object? Value,
        IReadOnlyList<ScriptNode> Children,
        int Line,
        int Column)
    {
        public string? TypeName { get; init; }
        public ScriptNode? Binding { get; init; }
        public IReadOnlyList<ScriptNode>? Parameters { get; init; }
    }
}

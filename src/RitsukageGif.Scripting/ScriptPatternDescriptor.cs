namespace RitsukageGif.Scripting
{
    public sealed record ScriptPatternDescriptor(
        string Name,
        string InputType,
        IReadOnlyList<string> PayloadTypes,
        ScriptPatternMatcher TryMatch);
}

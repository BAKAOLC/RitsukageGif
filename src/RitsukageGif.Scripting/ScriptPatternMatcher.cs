namespace RitsukageGif.Scripting
{
    public delegate bool ScriptPatternMatcher(object? value, out IReadOnlyList<object?> payload);
}

namespace RitsukageGif.Scripting
{
    public sealed record ScriptGenericTypeBinding(
        int Arity,
        Func<object?, IReadOnlyList<string>, Func<object?, string, object?>, object?> Validate,
        Func<IReadOnlyList<ScriptMemberDescriptor>, IReadOnlyList<string>, IReadOnlyList<ScriptMemberDescriptor>>?
            SpecializeMembers = null,
        int? OutcomeValueParameter = null,
        Func<object?, IReadOnlyList<string>, Func<object?, string, bool>, bool>? Matches = null);
}

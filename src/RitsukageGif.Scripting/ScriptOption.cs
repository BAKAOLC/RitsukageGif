namespace RitsukageGif.Scripting
{
    public sealed class ScriptOption(bool hasValue, object? value) : IScriptOutcome
    {
        public static ScriptOption None { get; } = new(false, null);
        bool IScriptOutcome.IsSuccess => HasValue;
        string IScriptOutcome.Family => "Option";
        public bool HasValue { get; } = hasValue;
        public object? Value { get; } = hasValue ? value : null;
    }
}

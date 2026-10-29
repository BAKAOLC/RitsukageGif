namespace RitsukageGif.Scripting
{
    public sealed class ScriptOption(bool hasValue, object? value) : IScriptOutcome
    {
        public static ScriptOption None { get; } = new(false, null);
        public bool HasValue { get; } = hasValue;
        bool IScriptOutcome.IsSuccess => HasValue;
        string IScriptOutcome.Family => "Option";
        public object? Value { get; } = hasValue ? value : null;
    }
}

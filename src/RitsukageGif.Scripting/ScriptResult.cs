namespace RitsukageGif.Scripting
{
    public sealed class ScriptResult(bool isOk, object? value) : IScriptOutcome
    {
        public bool IsOk { get; } = isOk;
        bool IScriptOutcome.IsSuccess => IsOk;
        string IScriptOutcome.Family => "Result";
        public object? Value { get; } = value;
    }
}

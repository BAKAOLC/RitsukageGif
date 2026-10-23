namespace RitsukageGif.Scripting
{
    public sealed class ScriptResult(bool isOk, object? value) : IScriptOutcome
    {
        bool IScriptOutcome.IsSuccess => IsOk;
        string IScriptOutcome.Family => "Result";
        public bool IsOk { get; } = isOk;
        public object? Value { get; } = value;
    }
}

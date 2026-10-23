namespace RitsukageGif.Scripting
{
    public interface IScriptOutcome
    {
        bool IsSuccess { get; }
        object? Value { get; }
        string Family { get; }
    }
}

namespace RitsukageGif.Scripting
{
    public sealed record ScriptDiagnostic(int Line, int Column, string Message, string? Source = null)
    {
        public IReadOnlyList<ScriptDiagnostic> Related { get; init; } = [];
    }
}

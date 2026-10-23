namespace RitsukageGif.Scripting
{
    public sealed record PresetDebugSnapshot(
        bool Paused,
        bool Finished,
        string? State,
        int Line,
        int Column,
        IReadOnlyDictionary<string, string> Variables,
        IReadOnlyList<string> Output,
        string? Error,
        string? Source = null,
        IReadOnlyList<string>? Frames = null,
        ScriptDiagnostic? ErrorDiagnostic = null,
        IReadOnlyList<ScriptStackFrame>? StackFrames = null);
}

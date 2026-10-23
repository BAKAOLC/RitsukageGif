namespace RitsukageGif.Scripting
{
    public sealed class ScriptExecutionException(
        int line,
        int column,
        string message,
        Exception? inner = null,
        string? sourceFile = null,
        IReadOnlyList<ScriptStackFrame>? frames = null)
        : Exception(UiText.Format("DslDiagnostic", line, column, message), inner)
    {
        public int Line { get; } = line;
        public int Column { get; } = column;
        public string? SourceFile { get; } = sourceFile;
        public string RawMessage { get; } = message;
        public IReadOnlyList<ScriptStackFrame> Frames { get; } = frames ?? [];
        public ScriptDiagnostic Diagnostic => new(Line, Column, RawMessage, SourceFile);
        public override string Message => SourceFile is null ? base.Message : SourceFile + ": " + base.Message;
    }
}

namespace RitsukageGif.Scripting
{
    public sealed class PresetSyntaxException(int line, int column, string message, string? sourceFile = null)
        : FormatException(UiText.Format("DslDiagnostic", line, column, message))
    {
        public int Line { get; } = line;
        public int Column { get; } = column;
        public string RawMessage { get; } = message;
        public string? SourceFile { get; } = sourceFile;
        public ScriptDiagnostic Diagnostic => new(Line, Column, RawMessage, SourceFile);
        public override string Message => SourceFile is null ? base.Message : SourceFile + ": " + base.Message;
    }
}

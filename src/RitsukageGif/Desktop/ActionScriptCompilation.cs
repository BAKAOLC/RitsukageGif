namespace RitsukageGif.Desktop
{
    internal static class ActionScriptCompilation
    {
        internal static string SourceFile => Path.Combine(Environment.CurrentDirectory, "action.rgscript");

        internal static ScriptNode Compile(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException(UiText.Get("ActionScriptRequired"));
            return ScriptEngine.Compile(source);
        }

        internal static bool IsEntryDiagnostic(ScriptDiagnostic diagnostic)
        {
            return diagnostic.Source is null || string.Equals(Path.GetFullPath(diagnostic.Source), SourceFile,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        internal static string FormatDiagnostic(ScriptDiagnostic diagnostic)
        {
            var location = $"{diagnostic.Line}:{diagnostic.Column}";
            if (!IsEntryDiagnostic(diagnostic)) location = Path.GetFileName(diagnostic.Source) + ":" + location;
            return location + " " + diagnostic.Message;
        }

        internal static ScriptDiagnostic Diagnostic(Exception error)
        {
            return error switch
            {
                PresetSyntaxException syntax => syntax.Diagnostic,
                ScriptExecutionException execution => execution.Diagnostic,
                _ => new(1, 1, error.Message),
            };
        }
    }
}

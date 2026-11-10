namespace RitsukageGif.Desktop
{
    internal sealed class ActionScriptRunner(
        Func<WindowPreset, PresetDebugger, PresetScriptSession> createSession)
    {
        internal object? LastResult { get; private set; }

        internal async Task<ScriptDiagnostic?> RunAsync(GlobalActionBinding action, PresetDebugger debugger,
            string? sourceFile = null,
            IReadOnlyDictionary<string, object?>? parameters = null, WindowPreset? context = null,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            sourceFile = Path.GetFullPath(sourceFile ?? ActionScriptCompilation.SourceFile);
            ScriptNode program;
            try
            {
                program = ActionScriptCompilation.Compile(action.Script);
            }
            catch (PresetSyntaxException error) when (error.SourceFile is null)
            {
                throw new PresetSyntaxException(error.Line, error.Column, error.RawMessage, sourceFile);
            }

            var basis = context ?? WindowPreset.ForWindow("*", "*") with { Absolute = true };
            var preset = basis with
            {
                Name = GlobalActions.Label(action), Program = program,
                SourceFile = sourceFile,
            };
            var session = createSession(preset, debugger);
            if (parameters is not null) session.Parameters.Update(parameters);
            await Task.Run(() => session.RunAsync(token), CancellationToken.None);
            token.ThrowIfCancellationRequested();
            LastResult = session.LastResult;
            return session.LastDiagnostic;
        }
    }
}

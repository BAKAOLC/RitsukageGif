namespace RitsukageGif.Desktop
{
    internal sealed class ActionScriptRunner(
        Func<WindowPreset, PresetDebugger, Action<ScriptBindingRegistry>, PresetScriptSession> createSession,
        Func<GlobalActionBinding, CancellationToken, Task> execute)
    {
        internal async Task<ScriptDiagnostic?> RunAsync(GlobalActionBinding action, PresetDebugger debugger,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var program = ActionScriptCompilation.Compile(action.Script);
            var preset = WindowPreset.ForWindow("*", "*") with
            {
                Name = GlobalActions.Label(action), Absolute = true, Program = program,
                SourceFile = ActionScriptCompilation.SourceFile,
            };
            var session = createSession(preset, debugger,
                bindings => ActionScriptLibrary.Register(bindings, execute));
            await Task.Run(() => session.RunAsync(token), CancellationToken.None);
            token.ThrowIfCancellationRequested();
            return session.LastDiagnostic;
        }
    }
}

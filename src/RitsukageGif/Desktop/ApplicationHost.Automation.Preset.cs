namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private Task<CommandReply> RunTaskPresetCommandAsync(string file, string[] arguments,
            CancellationToken token)
        {
            return RunCommandOperationAsync(Guid.NewGuid().ToString("N"), "preset", file, async operationToken =>
            {
                var debugger = new PresetDebugger { DefaultSource = file };
                return await RunOwnedPresetCommandAsync(debugger,
                    () => RunPresetWithParameters(file, arguments, debugger), operationToken);
            }, token);
        }

        private async Task<CommandReply> RunOwnedPresetCommandAsync(PresetDebugger debugger, Action activate,
            CancellationToken token)
        {
            if (_scriptCommand.Value is { } owner) _debuggerCommands[debugger] = owner;
            try
            {
                activate();
                var run = _presets.CaptureRun();
                var reply = await AwaitPresetCommandAsync(run, debugger, token);
                if (reply.State == "cancelled") throw new OperationCanceledException(reply.Message, run.Token);
                return reply;
            }
            finally
            {
                _debuggerCommands.TryRemove(debugger, out _);
            }
        }

        private async Task<CommandReply> AwaitPresetCommandAsync(PresetControllerRun run, PresetDebugger debugger,
            CancellationToken token)
        {
            var completion = run.Completion.Task;
            try
            {
                await completion.WaitAsync(token);
            }
            catch (OperationCanceledException) when
                (run.Token.IsCancellationRequested && !token.IsCancellationRequested)
            {
                return new(false, "cancelled", "The preset run was replaced or stopped.");
            }
            finally
            {
                if (token.IsCancellationRequested)
                    try
                    {
                        await StopScriptAsync(debugger);
                    }
                    finally
                    {
                        try
                        {
                            await completion;
                        }
                        catch (OperationCanceledException) when (completion.IsCanceled)
                        {
                        }
                    }
            }

            if (run.Token.IsCancellationRequested)
                return new(false, "cancelled", "The preset run was replaced or stopped.");
            return ScriptCommandReply(debugger, run.Script?.LastResult, debugger.Snapshot.ErrorDiagnostic,
                run.Failure ?? run.ScriptFailure?.Message ?? run.Script?.LastError ?? run.Machine.LastError);
        }
    }
}

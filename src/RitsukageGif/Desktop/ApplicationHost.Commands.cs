using System.Globalization;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private string CurrentState => _closing ? "closing" :
            _recording ? "recording" :
            _busy ? "processing" :
            _selecting ? "selecting" :
            _configuring ? "configuring" :
            _presets.Name is not null && _region is null ? "waiting" : "idle";

        internal async Task<CommandReply> ExecuteCommandAsync(string[] args)
        {
            try
            {
                if (_closing && args is not ["status"])
                    return new CommandReply(false, CurrentState, UiText.Get("CommandNotReady"));
                switch (args)
                {
                    case ["debug", "run", var debugFile, var debugName, var stopOnEntry]:
                        if (_busy || _selecting || _configuring)
                            return new CommandReply(false, CurrentState, UiText.Get("CommandNotReady"));
                        var debugger = new PresetDebugger();
                        if (stopOnEntry == "true") debugger.Pause();
                        StartScript(debugFile, debugName, debugger);
                        break;
                    case ["debug", "breakpoints", var sourceFile, var filePoints]:
                        _presets.Debugger?.SetBreakpoints(
                            filePoints.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse), sourceFile);
                        break;
                    case ["debug", "breakpoints", var points]:
                        _presets.Debugger?.SetBreakpoints(points.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(int.Parse));
                        break;
                    case ["debug", "pause"]: _presets.Debugger?.Pause(); break;
                    case ["debug", "continue"]: _presets.Debugger?.Continue(); break;
                    case ["debug", "step"]: _presets.Debugger?.Continue(true); break;
                    case ["debug", "stop"]:
                        await StopScriptAsync(_presets.Debugger);
                        break;
                    case []:
                    case ["show"]:
                        ShowMainWindow();
                        break;
                    case ["hide"]: View?.Hide(); break;
                    case ["status"]: break;
                    case ["input", .. var inputArguments]:
                        return await ExecuteInputCommandAsync(inputArguments);
                    case ["editor"]: await OpenEditorAsync(null); break;
                    case ["editor", var editorFile]: await OpenEditorAsync(editorFile); break;
                    case ["animation-editor"]: await ShowAnimationEditorAsync(); break;
                    case ["animation-editor", var animationFile]: await ShowAnimationEditorAsync(animationFile); break;
                    case ["stop"]:
                        if (_recording) await ToggleRecordingAsync();
                        break;
                    case ["cancel"]:
                        CancelSelection();
                        _abort?.Cancel();
                        break;
                    case ["start"]:
                        if (_busy || _selecting || _configuring || _region is null)
                            return new CommandReply(false, CurrentState, UiText.Get("CommandNotReady"));
                        _ = ToggleRecordingAsync();
                        break;
                    case ["select"]:
                        if (!CanEditRecordingSettings || _selecting || _configuring)
                            return new CommandReply(false, CurrentState, UiText.Get("CommandNotReady"));
                        _ = SelectAsync();
                        break;
                    case ["select", var x, var y, var width, var height]:
                        if (!CanEditRecordingSettings || _selecting || _configuring)
                            return new CommandReply(false, CurrentState, UiText.Get("CommandNotReady"));
                        DeactivatePreset();
                        Volatile.Write(ref _captureRegion, null);
                        _manualPreset = null;
                        SetRegion(new PixelRect(int.Parse(x, CultureInfo.InvariantCulture),
                            int.Parse(y, CultureInfo.InvariantCulture),
                            int.Parse(width, CultureInfo.InvariantCulture),
                            int.Parse(height, CultureInfo.InvariantCulture)));
                        break;
                    case ["select-window", var process]:
                        return await ExecuteCommandAsync(["select-window", process, "*"]);
                    case ["select-window", var process, var title]:
                        if (!CanEditRecordingSettings || _selecting || _configuring)
                            return new CommandReply(false, CurrentState, UiText.Get("CommandNotReady"));
                        await SelectWindowAsync(process, title);
                        break;
                    case ["preset", "activate", var name]:
                        if (_busy || _selecting || _configuring)
                            return new CommandReply(false, CurrentState, UiText.Get("CommandNotReady"));
                        ActivatePreset(PresetCatalog.Load(_settings.PresetFiles).Single(p => p.Name == name));
                        break;
                    case ["preset", "run", var file, .. var arguments]:
                        if (_busy || _selecting || _configuring)
                            return new CommandReply(false, CurrentState, UiText.Get("CommandNotReady"));
                        RunPresetWithParameters(file, arguments);
                        break;
                    case ["preset", "params", var session, .. var assignments]:
                        if (assignments.Length == 0)
                            throw new ArgumentException(UiText.Get("ScriptParametersInvalid"));
                        _presets.UpdateParameters(session, ScriptParameters.Parse(assignments));
                        break;
                    case ["preset", "stop"]:
                        if (_presets.ActivePreset is not null && _recording) await ToggleRecordingAsync();
                        DeactivatePreset();
                        Volatile.Write(ref _captureRegion, null);
                        break;
                    case ["presets"]:
                        return new CommandReply(true, CurrentState,
                            string.Join('\n', PresetCatalog.Load(_settings.PresetFiles).Select(p => p.Name)));
                    case ["windows"]:
                        var windows = await Task.Run(WindowLocator.List);
                        return new CommandReply(true, CurrentState,
                            string.Join('\n',
                                windows.Take(100).Select(w =>
                                    $"0x{w.Handle:X}\t{w.Process}\t{w.Title}\t{w.Bounds.X},{w.Bounds.Y},{w.Bounds.Width},{w.Bounds.Height}")));
                    case ["help"]:
                    case ["--help"]:
                        return new CommandReply(true, CurrentState, UiText.Get("CommandHelp"));
                    default: return new CommandReply(false, CurrentState, UiText.Get("CommandHelp"));
                }

                Notify();
                return new CommandReply(true, CurrentState, Output: _lastPath, Region: _region, Preset: _presets.Name,
                    Captured: _latestProgress?.Captured ?? 0, Encoded: _latestProgress?.Encoded ?? 0,
                    PresetState: _presets.State, Outputs: _lastOutputs, PresetError: _presets.LastError,
                    Debug: _presets.Debugger?.Snapshot, CapturedFps: _latestProgress?.CapturedFps ?? 0,
                    ElapsedSeconds: _latestProgress?.ElapsedSeconds ?? 0, ParameterSession: _presets.ParameterSession,
                    Streams: Volatile.Read(ref _streams));
            }
            catch (Exception ex)
            {
                return new CommandReply(false, CurrentState, ex.Message, Diagnostic: ex switch
                {
                    PresetSyntaxException syntax => syntax.Diagnostic,
                    ScriptExecutionException execution => execution.Diagnostic,
                    _ => null
                });
            }
        }
    }
}

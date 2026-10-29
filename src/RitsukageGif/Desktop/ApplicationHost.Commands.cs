using System.Globalization;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private string CurrentState => IsClosing ? "closing" :
            IsRecording ? "recording" :
            IsBusy ? "processing" :
            IsSelecting ? "selecting" :
            IsConfiguring ? "configuring" :
            _presets.Name is not null && Region is null ? "waiting" : "idle";

        internal async Task<CommandReply> ExecuteCommandAsync(string[] args)
        {
            var command = args.Length > 0 ? args[0] : "show";
            if (command != "status")
                HostLogger.ZLogInformation(
                    $"IPC command received. Command={command} ArgumentCount={args.Length} State={CurrentState}");
            try
            {
                if (IsClosing && args is not ["status"])
                    return new(false, CurrentState, UiText.Get("CommandNotReady"));
                switch (args)
                {
                    case ["debug", "run", var debugFile, var debugName, var stopOnEntry]:
                        if (IsBusy || IsSelecting || IsConfiguring)
                            return new(false, CurrentState, UiText.Get("CommandNotReady"));
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
                    case ["pins", "show"]:
                        if (IsSelecting) return new(false, CurrentState, UiText.Get("CommandNotReady"));
                        _pinnedImages.ShowAll();
                        break;
                    case ["pins", "hide"]:
                        if (IsSelecting) return new(false, CurrentState, UiText.Get("CommandNotReady"));
                        _pinnedImages.HideAll();
                        break;
                    case ["status"]: break;
                    case ["input", .. var inputArguments]:
                        return await ExecuteInputCommandAsync(inputArguments);
                    case ["editor"]: await OpenEditorAsync(null); break;
                    case ["editor", var editorFile]: await OpenEditorAsync(editorFile); break;
                    case ["animation-editor"]: await ShowAnimationEditorAsync(); break;
                    case ["animation-editor", var animationFile]: await ShowAnimationEditorAsync(animationFile); break;
                    case ["video-recorder"]: await OpenVideoRecorderAsync(); break;
                    case ["action", var actionId]:
                        var action = Settings.GlobalActions.SingleOrDefault(item => item.Id == actionId);
                        if (action is null || !CanExecuteAction(action))
                            return new(false, CurrentState, UiText.Get("CommandNotReady"));
                        _ = ExecuteActionAsync(action);
                        break;
                    case ["screenshot"]:
                        if (!CanTakeScreenshot)
                            return new(false, CurrentState, UiText.Get("CommandNotReady"));
                        _ = ScreenshotAsync();
                        break;
                    case ["screenshot", ("screen" or "window") and var screenshotSource]:
                        if (!CanTakeScreenshot)
                            return new(false, CurrentState, UiText.Get("CommandNotReady"));
                        _ = ScreenshotAsync(screenshotSource == "window"
                            ? CaptureSourceKind.Window
                            : CaptureSourceKind.Screen);
                        break;
                    case ["stop"]:
                        if (IsRecording) await ToggleRecordingAsync();
                        break;
                    case ["cancel"] when IsTakingScreenshot:
                        CancelScreenshot();
                        break;
                    case ["cancel"]:
                        CancelSelection();
                        if (_abort is { } abort) await abort.CancelAsync();
                        break;
                    case ["start"]:
                        if (IsBusy || IsSelecting || IsConfiguring || Region is null)
                            return new(false, CurrentState, UiText.Get("CommandNotReady"));
                        _ = ToggleRecordingAsync();
                        break;
                    case ["select"]:
                        if (!CanEditRecordingSettings || IsSelecting || IsConfiguring)
                            return new(false, CurrentState, UiText.Get("CommandNotReady"));
                        _ = SelectAsync();
                        break;
                    case ["select", var x, var y, var width, var height]:
                        if (!CanEditRecordingSettings || IsSelecting || IsConfiguring)
                            return new(false, CurrentState, UiText.Get("CommandNotReady"));
                        var selectedRegion = new PixelRect(int.Parse(x, CultureInfo.InvariantCulture),
                            int.Parse(y, CultureInfo.InvariantCulture),
                            int.Parse(width, CultureInfo.InvariantCulture),
                            int.Parse(height, CultureInfo.InvariantCulture));
                        ValidateRegion(selectedRegion, true);
                        DeactivatePreset();
                        Volatile.Write(ref _captureRegion, null);
                        _manualPreset = null;
                        SetRegion(selectedRegion);
                        Settings.CaptureSource = CaptureSourceKind.Screen;
                        break;
                    case ["select-window", var process]:
                        return await ExecuteCommandAsync(["select-window", process, "*"]);
                    case ["select-window", var process, var title]:
                        if (!CanEditRecordingSettings || IsSelecting || IsConfiguring)
                            return new(false, CurrentState, UiText.Get("CommandNotReady"));
                        await SelectWindowAsync(process, title);
                        break;
                    case ["preset", "activate", var name]:
                        if (IsBusy || IsSelecting || IsConfiguring)
                            return new(false, CurrentState, UiText.Get("CommandNotReady"));
                        ActivatePreset(PresetCatalog.Load(Settings.PresetFiles).Single(p => p.Name == name));
                        break;
                    case ["preset", "run", var file, .. var arguments]:
                        if (IsBusy || IsSelecting || IsConfiguring)
                            return new(false, CurrentState, UiText.Get("CommandNotReady"));
                        RunPresetWithParameters(file, arguments);
                        break;
                    case ["preset", "params", var session, .. var assignments]:
                        if (assignments.Length == 0)
                            throw new ArgumentException(UiText.Get("ScriptParametersInvalid"));
                        _presets.UpdateParameters(session, ScriptParameters.Parse(assignments));
                        break;
                    case ["preset", "stop"]:
                        if (_presets.ActivePreset is not null && IsRecording) await ToggleRecordingAsync();
                        DeactivatePreset();
                        Volatile.Write(ref _captureRegion, null);
                        break;
                    case ["presets"]:
                        return new(true, CurrentState,
                            string.Join('\n', PresetCatalog.Load(Settings.PresetFiles).Select(p => p.Name)));
                    case ["windows"]:
                        var windows = await Task.Run(WindowLocator.List);
                        return new(true, CurrentState,
                            string.Join('\n',
                                windows.Take(100).Select(w =>
                                    $"0x{w.Handle:X}\t{w.Process}\t{w.Title}\t{w.Bounds.X},{w.Bounds.Y},{w.Bounds.Width},{w.Bounds.Height}")));
                    case ["help"]:
                    case ["--help"]:
                        return new(true, CurrentState, UiText.Get("CommandHelp"));
                    default: return new(false, CurrentState, UiText.Get("CommandHelp"));
                }

                Notify();
                return new(true, CurrentState, Output: LastPath, Region: Region, Preset: _presets.Name,
                    Captured: LatestProgress?.Captured ?? 0, Encoded: LatestProgress?.Encoded ?? 0,
                    PresetState: _presets.State, Outputs: LastOutputs, PresetError: _presets.LastError,
                    Debug: _presets.Debugger?.Snapshot, CapturedFps: LatestProgress?.CapturedFps ?? 0,
                    ElapsedSeconds: LatestProgress?.ElapsedSeconds ?? 0, ParameterSession: _presets.ParameterSession,
                    Streams: Volatile.Read(ref _streams));
            }
            catch (Exception ex)
            {
                HostLogger.ZLogError(ex, $"IPC command failed. Command={command} State={CurrentState}");
                return new(false, CurrentState, ex.Message, Diagnostic: ex switch
                {
                    PresetSyntaxException syntax => syntax.Diagnostic,
                    ScriptExecutionException execution => execution.Diagnostic,
                    _ => null,
                });
            }
        }
    }
}

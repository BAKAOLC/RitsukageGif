using System.Globalization;
using Avalonia.Threading;

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

        internal async Task<CommandReply> ExecuteCommandAsync(string[] args, CancellationToken token = default)
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
                    case ["task", "execute", .. var taskArguments]:
                        if (!IsTaskHost)
                            return new(false, CurrentState, "This command requires an isolated task host.");
                        CompleteTaskStartupGuard();
                        var taskReply = await ExecuteCommandAsync(taskArguments, token);
                        TaskExitCode = taskReply.Success ? 0 :
                            taskReply.State == "cancelled" || token.IsCancellationRequested ? 130 : 1;
                        return taskReply with
                        {
                            SessionId = Program.TaskOptions?.SessionId,
                            ReplyWritten = Program.TaskOptions?.KeepAlive == true
                                ? taskReply.ReplyWritten
                                : () => Dispatcher.UIThread.Post(() => _ = ShutdownAsync()),
                            ReplyFailed = Program.TaskOptions?.KeepAlive == true
                                ? taskReply.ReplyFailed
                                : () => Dispatcher.UIThread.Post(() => _ = ShutdownAsync()),
                        };
                    case ["settings", "open"]: await OpenSettingsAsync(); break;
                    case ["logs", "path"]: return new(true, CurrentState, Output: DiagnosticsActions.LogDirectory);
                    case ["logs", "open"]: DiagnosticsActions.OpenLogs(); break;
                    case ["settings", .. var settingsArguments]:
                        return await ExecuteSettingsCommandAsync(settingsArguments, token);
                    case ["script", .. var scriptArguments]:
                        return await ExecuteScriptCommandAsync(scriptArguments, token);
                    case ["actions", .. var actionArguments]:
                        return await ExecuteActionsCommandAsync(actionArguments, token);
                    case ["exit"]:
                        return new(true, CurrentState)
                        {
                            ReplyWritten = () => Dispatcher.UIThread.Post(() => _ = ShutdownAsync()),
                        };
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
                    case ["pin-files", _, ..]:
                        if (IsSelecting) return new(false, CurrentState, UiText.Get("CommandNotReady"));
                        if (!await _pinnedImages.PinFilesAsync(args[1..], token))
                            return new(false, CurrentState, UiText.Get("DesktopToolbarDropUnsupported"));
                        break;
                    case ["pin-files-list", var shellList]:
                        return await ExecuteCommandAsync(Program.ReadShellFileList(shellList), token);
                    case ["pins", "hide"]:
                        if (IsSelecting) return new(false, CurrentState, UiText.Get("CommandNotReady"));
                        _pinnedImages.HideAll();
                        break;
                    case ["status"]: break;
                    case ["input", .. var inputArguments]:
                        return await ExecuteInputCommandAsync(inputArguments);
                    case ["editor"]: await OpenEditorAsync(null, token); break;
                    case ["editor", var editorFile]: await OpenEditorAsync(editorFile, token); break;
                    case ["animation-editor"]: await ShowAnimationEditorAsync(token: token); break;
                    case ["animation-editor", var animationFile]:
                        await ShowAnimationEditorAsync(animationFile, token); break;
                    case ["video-recorder"]: await OpenVideoRecorderAsync(); break;
                    case ["action", var actionId]:
                        return await ExecuteActionsCommandAsync(["run", "id", actionId], token);
                    case ["screenshot"]:
                        if (!CanTakeScreenshot)
                            return new(false, CurrentState, UiText.Get("CommandNotReady"));
                        var screenshot = await ExecuteScreenshotActionAsync(
                            new() { Kind = GlobalActionKind.Screenshot },
                            propagateErrors: true, token: token);
                        return ScreenshotReply(screenshot);
                    case ["screenshot", ("screen" or "window") and var screenshotSource]:
                        if (!CanTakeScreenshot)
                            return new(false, CurrentState, UiText.Get("CommandNotReady"));
                        var sourceScreenshot = await ExecuteScreenshotActionAsync(
                            new() { Kind = GlobalActionKind.Screenshot, Parameter = screenshotSource },
                            propagateErrors: true, token: token);
                        return ScreenshotReply(sourceScreenshot);
                    case ["stop"]:
                        await StopActiveRecordingAsync();
                        break;
                    case ["cancel"] when IsTakingScreenshot:
                        CancelScreenshot();
                        break;
                    case ["cancel"]:
                        CancelSelection();
                        if (_abort is { } abort) await abort.CancelAsync();
                        break;
                    case ["start"]:
                        if (!CanToggleRecording || IsRecording)
                            return new(false, CurrentState, UiText.Get("CommandNotReady"));
                        await RunRecordingCommandAsync(token);
                        break;
                    case ["select"]:
                        if (!CanEditRecordingSettings || IsSelecting || IsConfiguring)
                            return new(false, CurrentState, UiText.Get("CommandNotReady"));
                        await SelectAsync(token);
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
                        return await ExecuteCommandAsync(["select-window", process, "*"], token);
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
                        if (IsTaskHost) return await RunTaskPresetCommandAsync(file, arguments, token);
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
                        var windows = await Task.Run(WindowLocator.List, token);
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
            catch (OperationCanceledException error) when (token.IsCancellationRequested ||
                                                           error.CancellationToken.IsCancellationRequested)
            {
                return new(false, "cancelled", "Operation cancelled.");
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

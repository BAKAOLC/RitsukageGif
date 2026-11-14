using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private readonly Dictionary<string, CommandOperation> _commandOperations = new(StringComparer.Ordinal);
        private readonly AsyncLocal<CommandOperation?> _scriptCommand = new();
        private readonly ConcurrentDictionary<PresetDebugger, CommandOperation> _debuggerCommands = new();

        private void ConfigureAutomationBindings(ScriptBindingRegistry bindings, string? sourceFile,
            ScriptResourceScope resources, PresetDebugger? debugger)
        {
            ConfigureAutomationBindings(bindings, sourceFile, resources, debugger, null);
        }

        private void ConfigureAutomationBindings(ScriptBindingRegistry bindings, string? sourceFile,
            ScriptResourceScope resources, PresetDebugger? debugger, ActionScriptCallPath? callPath)
        {
            var directory = Path.GetDirectoryName(sourceFile ?? ActionScriptCompilation.SourceFile);
            var owner = debugger is not null && _debuggerCommands.TryGetValue(debugger, out var commandOwner)
                ? commandOwner
                : _scriptCommand.Value;
            AutomationScriptLibrary.Register(bindings,
                (action, token) => ExecuteScriptActionAsync(action, callPath, owner, token), PinScriptFilesAsync, this,
                directory,
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    if (owner is null) _ = ExitAfterScriptResourcesAsync();
                    else owner.ExitRequested = true;
                    return Task.CompletedTask;
                }, resources.Own(new AnnotationScriptService(directory, CaptureAnnotationSourceAsync)),
                resources.Own(new AnimationEditingService((path, token) =>
                    AnimationImportProcess.OpenAsync(path, token: token))),
                resources.Own(new LongCaptureService(CreateLongCaptureSourceAsync)),
                _pinnedImages.CreateAutomationService(),
                new ScriptTextTranslationService(() => Settings.Translation with { }),
                () => Settings.GlobalActions,
                async token => await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    token.ThrowIfCancellationRequested();
                    await OpenSettingsAsync();
                }), () => DiagnosticsActions.LogDirectory, DiagnosticsActions.OpenLogs, this, _pinnedImages);
        }

        private async Task<RecordingOutcome> RunRecordingCommandAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (IsClosing || _screenshotRecordingActive || _screenshotRecordingPreparing)
                throw new InvalidOperationException(UiText.Get("CommandNotReady"));
            var stopping = IsRecording;
            Task<RecordingOutcome> operation;
            if (stopping)
            {
                operation = _operation ?? throw new InvalidOperationException(UiText.Get("CommandNotReady"));
            }
            else
            {
                if (IsSelecting || IsConfiguring || IsBusy || Region is not { } region)
                    throw new InvalidOperationException(UiText.Get("CommandNotReady"));
                View?.SaveCommon();
                operation = StartAnimationRecordingCore(region);
            }

            var abort = _abort;
            var completed = false;
            await using var registration = token.Register(() => Dispatcher.UIThread.Post(() =>
            {
                // ReSharper disable once AccessToModifiedClosure
                if (!completed && ReferenceEquals(_abort, abort)) abort?.Cancel();
            }));
            try
            {
                if (stopping) await ToggleRecordingAsync();
                var outcome = await operation;
                token.ThrowIfCancellationRequested();
                if (outcome.Cancelled)
                    throw new OperationCanceledException(UiText.Get("Cancelled"), new CancellationToken(true));
                outcome.EnsureCompleted();
                return outcome;
            }
            finally
            {
                completed = true;
            }
        }

        private async Task ExitAfterScriptResourcesAsync()
        {
            try
            {
                var tasks = await Dispatcher.UIThread.InvokeAsync(() =>
                    _actionScriptTasks.ToArray().Append(_presets.ScriptCompletion).ToArray());
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) when (_hostCancellation.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                HostLogger.ZLogWarning(
                    $"Script group completed with a failure before requested exit. ExceptionType={error.GetType().Name}");
            }
            finally
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!IsClosing) _ = ShutdownAsync();
                });
            }
        }

        private async Task<CommandReply> RunCommandOperationAsync(string id, string kind, string? source,
            Func<CancellationToken, Task<CommandReply>> execute, CancellationToken token)
        {
            if (_commandOperations.ContainsKey(id)) return new(false, CurrentState, "Operation ID is already running.");
            // Eight IPC slots exist; retaining four slots allows status/cancellation while operations await user input.
            if (_commandOperations.Count >= 4)
                return new(false, CurrentState, "Four command operations are already running.");
            using var operation = new CommandOperation(id, kind, source,
                CancellationTokenSource.CreateLinkedTokenSource(token, _hostCancellation.Token));
            _commandOperations.Add(id, operation);
            var previous = _scriptCommand.Value;
            _scriptCommand.Value = operation;
            try
            {
                var reply = await execute(operation.Cancellation.Token);
                return reply with
                {
                    OperationId = id,
                    ReplyWritten = operation.ExitRequested
                        ? () => Dispatcher.UIThread.Post(() => _ = ShutdownAsync())
                        : reply.ReplyWritten,
                };
            }
            catch (OperationCanceledException error) when (operation.Cancellation.IsCancellationRequested ||
                                                           error.CancellationToken.IsCancellationRequested)
            {
                return new(false, "cancelled", "Operation cancelled.", OperationId: id);
            }
            catch (Exception error)
            {
                HostLogger.ZLogWarning($"Command operation failed. Kind={kind} ExceptionType={error.GetType().Name}");
                return new(false, CurrentState, error.Message, Diagnostic: error switch
                {
                    PresetSyntaxException syntax => syntax.Diagnostic.Source is null
                        ? syntax.Diagnostic with { Source = source }
                        : syntax.Diagnostic,
                    ScriptExecutionException execution => execution.Diagnostic,
                    _ => null,
                }, OperationId: id);
            }
            finally
            {
                _scriptCommand.Value = previous;
                _commandOperations.Remove(id);
                operation.Completion.TrySetResult();
            }
        }

        private async Task<CommandReply> ExecuteScriptCommandAsync(string[] args, CancellationToken token)
        {
            switch (args)
            {
                case ["list"]:
                    return new(true, CurrentState,
                        Operations: [.. _commandOperations.Values.Select(item => item.Info)]);
                case ["cancel", var id]:
                    if (!_commandOperations.TryGetValue(id, out var running))
                        return new(false, CurrentState, "Operation does not exist.");
                    await running.Cancellation.CancelAsync();
                    await running.Completion.Task.WaitAsync(token);
                    return new(true, CurrentState, OperationId: id);
                case ["execute", var id, var file, var presetName, var text, .. var assignments]:
                    return await RunCommandOperationAsync(id, "script", file, async operationToken =>
                    {
                        var debugger = new PresetDebugger { DefaultSource = file };
                        if (_scriptCommand.Value is { } commandOwner) _debuggerCommands[debugger] = commandOwner;
                        try
                        {
                            var parameters = ScriptParameters.Parse(assignments);
                            if (ScriptSource.IsPresetDocument(text))
                            {
                                if (IsBusy || IsSelecting || IsConfiguring)
                                    throw new InvalidOperationException(UiText.Get("CommandNotReady"));
                                var presets = WindowPreset
                                    .Parse(text, AutomationScriptLibrary.CreateBindings(Path.GetDirectoryName(file)))
                                    .Select(item => PresetCatalog.Resolve(item, file)).ToArray();
                                var preset = presetName.Length == 0
                                    ? presets.Single()
                                    : presets.Single(item => item.Name == presetName);
                                ActivatePreset(preset, debugger, parameters);
                                return await AwaitPresetCommandAsync(_presets.CaptureRun(), debugger,
                                    operationToken);
                            }

                            if (presetName.Length != 0)
                                throw new ArgumentException("--preset requires a preset document.");
                            var action = new GlobalActionBinding
                            {
                                Id = id, Kind = GlobalActionKind.RunScript, Name = "Command script", Script = text,
                            };
                            object? result = null;
                            var diagnostic = await RunAutomationScriptAsync(action, debugger, file,
                                parameters, value => result = value, operationToken);
                            return ScriptCommandReply(debugger, result, diagnostic);
                        }
                        finally
                        {
                            _debuggerCommands.TryRemove(debugger, out _);
                        }
                    }, token);
                default:
                    return new(false, CurrentState,
                        "Use script file PATH, script eval TEXT, script stdin, script list, or script cancel ID.");
            }
        }

        private CommandReply ScriptCommandReply(PresetDebugger debugger, object? result,
            ScriptDiagnostic? diagnostic, string? error = null)
        {
            try
            {
                return new(diagnostic is null && error is null, CurrentState, error ?? debugger.Snapshot.Error ?? "",
                    Region: Region, Debug: debugger.Snapshot, Diagnostic: diagnostic,
                    Value: ScriptValueJson.ToElement(result));
            }
            catch (ScriptValueSerializationException failure)
            {
                return new(false, CurrentState, failure.Message, Diagnostic: diagnostic,
                    Value: ScriptValueJson.Error(failure));
            }
        }

        private CommandActionInfo DescribeAction(GlobalActionBinding action)
        {
            var available = CanExecuteAction(action);
            return new(action.Id, action.Kind.ToString(), GlobalActions.Label(action), action.Enabled, available,
                GlobalActions.Definition(action.Kind).FileParameter, action.Parameter, action.PresetName,
                new Dictionary<string, string>(action.Arguments), available ? null : UiText.Get("CommandNotReady"));
        }

        private async Task<CommandReply> ExecuteActionsCommandAsync(string[] args, CancellationToken token)
        {
            switch (args)
            {
                case ["list"]:
                    var actions = GlobalActions.Catalog.Select(definition => DescribeAction(new()
                        {
                            Id = "", Kind = definition.Kind, ShowInTray = false,
                        }) with
                        {
                            Id = null,
                        }).Concat(Settings.GlobalActions.Select(DescribeAction)).ToArray();
                    return new(true, CurrentState, Actions: actions);
                case ["get", var id]:
                    var found = Settings.GlobalActions.SingleOrDefault(action => action.Id == id);
                    return found is null
                        ? new(false, CurrentState, "Action does not exist.")
                        : new(true, CurrentState, Actions: [DescribeAction(found)]);
            }

            if (args is not ["run", ("kind" or "id") and var selector, var name, .. var options])
                return new(false, CurrentState, "Use actions list, actions get ID, or actions run kind KIND | id ID.");
            GlobalActionBinding selected;
            if (selector == "id")
            {
                var existing = Settings.GlobalActions.SingleOrDefault(action => action.Id == name);
                if (existing is null) return new(false, CurrentState, "Action does not exist.");
                selected = existing with
                {
                    Arguments = new(existing.Arguments), IgnoredPrograms = [.. existing.IgnoredPrograms],
                };
            }
            else
            {
                if (!Enum.TryParse<GlobalActionKind>(name, true, out var kind) || !Enum.IsDefined(kind))
                    return new(false, CurrentState, "Action kind does not exist.");
                selected = new() { Kind = kind, ShowInTray = false };
            }

            var operationId = Guid.NewGuid().ToString("N");
            for (var index = 0; index < options.Length; index += 2)
            {
                if (index + 1 == options.Length) return new(false, CurrentState, "An option value is missing.");
                switch (options[index])
                {
                    case "--parameter": selected.Parameter = options[index + 1]; break;
                    case "--preset": selected.PresetName = options[index + 1]; break;
                    case "--id": operationId = options[index + 1]; break;
                    case "--arg":
                        foreach (var (key, value) in ScriptParameters.Parse([options[index + 1]]))
                            selected.Arguments[key] = value switch
                            {
                                null => "null", bool boolean => boolean ? "true" : "false",
                                double number => number.ToString("R", CultureInfo.InvariantCulture),
                                _ => (string)value,
                            };
                        break;
                    default: return new(false, CurrentState, "Unknown action option.");
                }
            }

            var errors = GlobalActions.Validate([selected], []);
            if (errors.Count != 0) return new(false, CurrentState, string.Join(Environment.NewLine, errors));
            if (!CanExecuteAction(selected)) return new(false, CurrentState, UiText.Get("CommandNotReady"));
            return await RunCommandOperationAsync(operationId, "action", null, async operationToken =>
            {
                CommandReply? ownedReply = null;
                var screenshot = await ExecuteActionCoreAsync(selected, operationToken, true, true,
                    reply => ownedReply = reply);
                return screenshot is null
                    ? ownedReply ?? new(true, CurrentState, Region: Region)
                    : ScreenshotReply(screenshot);
            }, token);
        }
    }
}

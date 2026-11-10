using System.Diagnostics;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private readonly CancellationTokenSource _actionScriptCancellation = new();
        private readonly HashSet<Task> _actionScriptTasks = [];
        private readonly AsyncLocal<ActionScriptCallPath?> _actionScriptCallPath = new();

        private async Task<ScriptDiagnostic?> RunActionScriptAsync(GlobalActionBinding action,
            PresetDebugger debugger, CancellationToken token)
        {
            var previous = _actionScriptCallPath.Value;
            if (previous?.Contains(action.Id) == true)
                throw new InvalidOperationException("The configured action call chain contains a cycle.");
            _actionScriptCallPath.Value = new(action.Id, previous);
            try
            {
                return await RunAutomationScriptAsync(action, debugger, token: token);
            }
            finally
            {
                _actionScriptCallPath.Value = previous;
            }
        }

        private async Task<ScriptDiagnostic?> RunAutomationScriptAsync(GlobalActionBinding action,
            PresetDebugger debugger, string? sourceFile = null,
            IReadOnlyDictionary<string, object?>? parameters = null, Action<object?>? result = null,
            CancellationToken token = default)
        {
            if (IsClosing) throw new OperationCanceledException(_actionScriptCancellation.Token);
            using var lifetime =
                CancellationTokenSource.CreateLinkedTokenSource(token, _actionScriptCancellation.Token);
            var scriptToken = lifetime.Token;
            var callPath = _actionScriptCallPath.Value;
            var runner = new ActionScriptRunner((preset, sessionDebugger) =>
                CreateActionScriptSession(preset, sessionDebugger, callPath, scriptToken));
            var task = runner.RunAsync(action, debugger, sourceFile, parameters,
                _presets.ActivePreset ?? _manualPreset, scriptToken);
            _actionScriptTasks.Add(task);
            try
            {
                var diagnostic = await task;
                result?.Invoke(runner.LastResult);
                return diagnostic;
            }
            finally
            {
                _actionScriptTasks.Remove(task);
            }
        }

        private PresetScriptSession CreateActionScriptSession(WindowPreset preset, PresetDebugger debugger,
            ActionScriptCallPath? callPath, CancellationToken token)
        {
            var clock = Stopwatch.StartNew();
            var resources = new ScriptResourceScope();
            var text = new OcrRecognitionService();
            var barcode = new BarcodeRecognitionService();
            return new(preset,
                async () => await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var target = _presets.Target;
                    var window = target is not null
                        ? new(target.Handle, target.Title, target.Bounds)
                        : Region is { IsEmpty: false } region
                            ? new PresetWindowState(0, string.Empty, region)
                            : null;
                    return new PresetContext(window, IsRecording, IsBusy, clock.Elapsed.TotalSeconds,
                        _recordClock.Elapsed.TotalSeconds, preset.Constants,
                        Outputs: [.. LastOutputs], Streams: Volatile.Read(ref _streams));
                }),
                async command =>
                    await Dispatcher.UIThread.InvokeAsync(() => ApplyActionScriptCommandAsync(command, token)),
                debugger,
                bindings =>
                {
                    ConfigureAutomationBindings(bindings, preset.SourceFile, resources, debugger, callPath);
                },
                mediaService: new MediaProcessingService(), imageService: new ImageCompositionService(),
                inputReader: _inputs, staticCaptureService: this, videoService: this,
                textRecognitionService: text, barcodeRecognitionService: barcode,
                privacyService: new PrivacyDetectionService([
                    new OcrPrivacyCandidateProvider(text), new BarcodePrivacyCandidateProvider(barcode),
                ]),
                documentService: new PdfDocumentService(), tableService: StructuredRecognitionProcess.Shared,
                formulaService: StructuredRecognitionProcess.Shared,
                formulaOutputService: StructuredRecognitionProcess.Shared,
                tableOutputService: new TableOutputService(), formulaDocumentService: new FormulaDocxService(),
                tableDataService: new TableDataService(), tableEditService: TableEditorService.Shared,
                manageRecordingLifecycle: false, sceneHostProvider: () => _presets.ActiveScript,
                resourceScope: resources);
        }

        private async Task ApplyActionScriptCommandAsync(PresetAction action, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var operation = IsBusy && action.Kind is "stop" or "cancel" && _operation is { IsCompleted: false }
                ? _operation
                : null;
            await ApplyPresetActionAsync(action, Region, false);
            if (operation is null) return;
            var outcome = await operation.WaitAsync(token);
            if (action.Kind == "stop") outcome.EnsureCompleted();
        }

        private async Task<ScreenshotInvocation?> ExecuteScriptActionAsync(GlobalActionBinding action,
            ActionScriptCallPath? callPath, CommandOperation? commandOwner, CancellationToken token)
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var previous = _actionScriptCallPath.Value;
                var previousCommand = _scriptCommand.Value;
                _actionScriptCallPath.Value = callPath;
                _scriptCommand.Value = commandOwner;
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (!CanExecuteAction(action)) throw new InvalidOperationException(UiText.Get("CommandNotReady"));
                    var result = await ExecuteActionCoreAsync(action, token, true,
                        true);
                    token.ThrowIfCancellationRequested();
                    return result;
                }
                finally
                {
                    _actionScriptCallPath.Value = previous;
                    _scriptCommand.Value = previousCommand;
                }
            });
        }

        private async Task StopActionScriptsAsync()
        {
            await _actionScriptCancellation.CancelAsync();
            try
            {
                await Task.WhenAll(_actionScriptTasks.ToArray());
            }
            catch (OperationCanceledException) when (_actionScriptCancellation.IsCancellationRequested)
            {
            }
            finally
            {
                _actionScriptCancellation.Dispose();
            }
        }
    }
}

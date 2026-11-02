using System.Diagnostics;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private readonly CancellationTokenSource _actionScriptCancellation = new();
        private readonly HashSet<Task> _actionScriptTasks = [];

        private async Task<ScriptDiagnostic?> RunActionScriptAsync(GlobalActionBinding action,
            PresetDebugger debugger, CancellationToken token)
        {
            if (IsClosing) throw new OperationCanceledException(_actionScriptCancellation.Token);
            using var lifetime =
                CancellationTokenSource.CreateLinkedTokenSource(token, _actionScriptCancellation.Token);
            var scriptToken = lifetime.Token;
            var runner = new ActionScriptRunner(
                (preset, sessionDebugger, bindings) => CreateActionScriptSession(preset, sessionDebugger, bindings,
                    scriptToken), ExecuteScriptActionAsync);
            var task = runner.RunAsync(action, debugger, scriptToken);
            _actionScriptTasks.Add(task);
            try
            {
                return await task;
            }
            finally
            {
                _actionScriptTasks.Remove(task);
            }
        }

        private PresetScriptSession CreateActionScriptSession(WindowPreset preset, PresetDebugger debugger,
            Action<ScriptBindingRegistry> configureBindings, CancellationToken token)
        {
            var clock = Stopwatch.StartNew();
            var text = new OcrRecognitionService();
            var barcode = new BarcodeRecognitionService();
            return new(preset,
                async () => await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var target = _presets.Target;
                    var window = target is null
                        ? null
                        : new PresetWindowState(target.Handle, target.Title, target.Bounds);
                    return new PresetContext(window, IsRecording, IsBusy, clock.Elapsed.TotalSeconds,
                        _recordClock.Elapsed.TotalSeconds, preset.Constants,
                        Outputs: [.. LastOutputs], Streams: Volatile.Read(ref _streams));
                }),
                async command =>
                    await Dispatcher.UIThread.InvokeAsync(() => ApplyActionScriptCommandAsync(command, token)),
                debugger, configureBindings,
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
                manageRecordingLifecycle: false);
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

        private async Task ExecuteScriptActionAsync(GlobalActionBinding action, CancellationToken token)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                if (!CanExecuteAction(action)) throw new InvalidOperationException(UiText.Get("CommandNotReady"));
                await ExecuteActionCoreAsync(action);
                token.ThrowIfCancellationRequested();
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

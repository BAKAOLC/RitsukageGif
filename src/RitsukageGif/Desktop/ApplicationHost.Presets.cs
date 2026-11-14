using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private PresetEditorWindow? _editor;

        private void DeactivatePreset()
        {
            _captureDefinitions = new();
            var hadPreset = _presets.ActivePreset is not null;
            _presets.Dispose();
            Volatile.Write(ref _captureRegion, null);
            if (!hadPreset) return;
            Region = null;
            Notify();
        }

        private void ActivatePreset(WindowPreset preset, PresetDebugger? debugger = null,
            IReadOnlyDictionary<string, object?>? parameters = null)
        {
            _captureDefinitions = new();
            Region = null;
            _manualPreset = null;
            Volatile.Write(ref _captureRegion, null);
            _presets.Activate(preset, debugger, parameters);
            Notify();
        }

        internal async Task OpenSettingsEditorAsync(Window owner, string file)
        {
            var editor = _editor;
            if (editor is null)
            {
                editor = new(this, file);
                _editor = editor;
                editor.Closed += (_, _) => _editor = null;
            }
            else
            {
                await editor.OpenLocationAsync(file);
            }

            if (IsClosing || !ReferenceEquals(_editor, editor)) return;
            editor.SetSettingsMode(true);
            if (editor.IsVisible) editor.Hide();
            try
            {
                await editor.ShowDialog(owner);
            }
            finally
            {
                editor.SetSettingsMode(false);
            }
        }

        internal void OpenEditor(string? file)
        {
            _ = OpenEditorSafelyAsync(file);
        }

        private async Task OpenEditorSafelyAsync(string? file)
        {
            try
            {
                await OpenEditorAsync(file);
            }
            catch (OperationCanceledException) when (IsClosing)
            {
            }
            catch (Exception error)
            {
                SetStatus(error.Message);
            }
        }

        private async Task OpenEditorAsync(string? file, CancellationToken token = default)
        {
            if (IsClosing) return;
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _hostCancellation.Token);
            token = lifetime.Token;
            token.ThrowIfCancellationRequested();
            var initialText = _editor is null && file is not null && !Directory.Exists(file)
                ? await PresetEditorWindow.ReadEditorFileAsync(file, token)
                : null;
            token.ThrowIfCancellationRequested();
            if (_editor is { } existing)
            {
                if (file is not null) await existing.OpenLocationAsync(file, initialText, token);
                token.ThrowIfCancellationRequested();
                if (IsClosing || !ReferenceEquals(_editor, existing)) return;
                existing.Show();
                existing.Activate();
                return;
            }

            _editor = new(this, file, initialText);
            _editor.Closed += (_, _) => _editor = null;
            _editor.Show();
        }

        internal void StartScript(string file, string name, PresetDebugger debugger)
        {
            if (IsBusy || IsSelecting || IsConfiguring || IsClosing)
                throw new InvalidOperationException(UiText.Get("CommandNotReady"));
            var presets = PresetCatalog.Load([file]);
            var preset = string.IsNullOrEmpty(name) ? presets[0] : presets.Single(p => p.Name == name);
            debugger.DefaultSource = Path.GetFullPath(file);
            ActivatePreset(preset, debugger);
        }

        internal async Task StopScriptAsync(PresetDebugger? debugger)
        {
            if (debugger is null || !ReferenceEquals(debugger, _presets.Debugger)) return;
            var operation = _presets.CaptureRun().RecordingOperation;
            var cancellation = OwnsActiveRecording(operation) && _abort is { } abort
                ? abort.CancelAsync()
                : Task.CompletedTask;
            DeactivatePreset();
            try
            {
                await cancellation;
            }
            finally
            {
                if (operation is not null) await operation;
            }
        }

        private bool OwnsActiveRecording(Task<RecordingOutcome>? operation)
        {
            return operation is { IsCompleted: false } && ReferenceEquals(operation, _operation);
        }

        private void SetRegion(PixelRect region, bool requireMonitor = true)
        {
            ValidateRegion(region, requireMonitor);
            Region = region;
            Notify();
        }

        private void ValidateRegion(PixelRect region, bool requireMonitor, int? divisor = null)
        {
            new RecordingOptions(region, Divisor: divisor ?? Settings.Divisor).Validate();
            if (requireMonitor && Screens.All.All(s =>
                    region.Intersect(new(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height)).IsEmpty))
                throw new ArgumentException(UiText.Get("NoMonitor"));
        }

        private Task ApplyPresetActionAsync(PresetAction action, PixelRect? region)
        {
            return ApplyPresetActionAsync(action, region, true);
        }

        private async Task ApplyPresetActionAsync(PresetAction action, PixelRect? region, bool waitForOutput)
        {
            if (IsClosing) return;
            var owner = waitForOutput && _presets.ActivePreset is not null ? _presets.CaptureRun() : null;
            switch (action.Kind)
            {
                case "startMany":
                    var multiple = StartMultiple(action.Outputs ??
                                                 throw new ArgumentException(UiText.Get("InvalidRecording")));
                    if (owner is not null)
                    {
                        owner.RecordingOperation = multiple;
                        owner.RecordingExplicitlyCancelled = false;
                    }

                    return;
                case "select":
                case "follow":
                    switch (region)
                    {
                        case null when action.Kind == "select":
                            throw new InvalidOperationException(UiText.Get("CommandNotReady"));
                        case { } selected:
                            Volatile.Write(ref _captureRegion,
                                new(selected, _presets.Target?.Handle ?? 0, _presets.ActivePreset));
                            if (!IsBusy)
                                SetRegion(selected,
                                    _presets.ActivePreset is { Source: CaptureSourceKind.Screen, Layout: null });
                            break;
                    }

                    return;
                case "copy":
                    if (_operation is { } output)
                    {
                        if (!output.IsCompleted) throw new InvalidOperationException(UiText.Get("CommandNotReady"));
                        (await output).EnsureCompleted();
                    }

                    await CopyAsync();
                    return;
                case "start":
                    if (!CanToggleRecording || IsRecording || Region is not { } recordingRegion)
                        throw new InvalidOperationException(UiText.Get("CommandNotReady"));
                    View?.SaveCommon();
                    var started = StartAnimationRecordingCore(recordingRegion, action.Settings);
                    if (owner is not null)
                    {
                        owner.RecordingOperation = started;
                        owner.RecordingExplicitlyCancelled = false;
                    }

                    if (started.IsCompleted) (await started).EnsureCompleted();
                    return;
            }

            switch (action.Kind)
            {
                case "finish" or "stop" or "cancel" when owner is not null:
                    await CompletePresetRecordingAsync(owner, action.Kind);
                    return;
                default:
                    var reply = await ExecuteCommandAsync([action.Kind == "finish" ? "stop" : action.Kind]);
                    if (!reply.Success) throw new InvalidOperationException(reply.Message);
                    return;
            }
        }

        private async Task CompletePresetRecordingAsync(PresetControllerRun owner, string action)
        {
            if (owner.RecordingOperation is not { } operation) return;
            if (OwnsActiveRecording(operation))
            {
                if (action == "cancel")
                {
                    owner.RecordingExplicitlyCancelled = true;
                    if (_abort is { } abort) await abort.CancelAsync();
                }
                else if (IsRecording)
                {
                    await ToggleRecordingAsync();
                }
            }

            if (action != "finish" && owner.Preset.Program is null) return;
            var outcome = await operation;
            if (action == "stop" || (action == "finish" && (!outcome.Cancelled || !owner.RecordingExplicitlyCancelled)))
                outcome.EnsureCompleted();
        }

        private sealed record CaptureRegion(PixelRect Region, nint Handle, WindowPreset? Preset)
        {
            public PixelRect? Resolve()
            {
                return Preset is { Follow: true } && Handle != 0
                    ? WindowLocator.ResolveRegion(Handle, Preset)
                    : Region;
            }
        }
    }
}

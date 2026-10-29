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
            if (_editor is null)
            {
                _editor = new(this, file);
                _editor.Closed += (_, _) => _editor = null;
            }
            else
            {
                await _editor.OpenLocationAsync(file);
            }

            var editor = _editor;
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
            catch (Exception error)
            {
                SetStatus(error.Message);
            }
        }

        private async Task OpenEditorAsync(string? file)
        {
            if (_editor is not null)
            {
                if (file is not null) await _editor.OpenLocationAsync(file);
                _editor.Show();
                _editor.Activate();
                return;
            }

            _editor = new(this, file);
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
            if (_abort is { } abort) await abort.CancelAsync();
            if (_stop is { } stop) await stop.CancelAsync();
            DeactivatePreset();
            if (_operation is not null) await _operation;
            Volatile.Write(ref _captureRegion, null);
        }

        private void SetRegion(PixelRect region, bool requireMonitor = true)
        {
            ValidateRegion(region, requireMonitor);
            Region = region;
            Notify();
        }

        private void ValidateRegion(PixelRect region, bool requireMonitor)
        {
            new RecordingOptions(region, Divisor: Settings.Divisor).Validate();
            if (requireMonitor && Screens.All.All(s =>
                    region.Intersect(new(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height)).IsEmpty))
                throw new ArgumentException(UiText.Get("NoMonitor"));
        }

        private async Task ApplyPresetActionAsync(PresetAction action, PixelRect? region)
        {
            if (IsClosing) return;
            switch (action.Kind)
            {
                case "startMany":
                    StartMultiple(action.Outputs ?? throw new ArgumentException(UiText.Get("InvalidRecording")));
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
                    _startOverrides = action.Settings;
                    break;
            }

            var reply = await ExecuteCommandAsync([action.Kind == "finish" ? "stop" : action.Kind]);
            if (!reply.Success)
            {
                _startOverrides = null;
                throw new InvalidOperationException(reply.Message);
            }

            if (action.Kind is "stop" or "cancel" && _presets.ActivePreset?.Program is not null &&
                _operation is { } operation)
            {
                // Cancellation also must drain the operation before a subsequent script action may run.
                // ReSharper disable once InlineTemporaryVariable
                var outcome = await operation;
                if (action.Kind == "stop") outcome.EnsureCompleted();
            }
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

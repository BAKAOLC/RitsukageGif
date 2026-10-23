using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class PresetController(
        Func<PresetAction, PixelRect?, Task> execute,
        Func<(bool Recording, bool Busy, double RecordingElapsed)> recording,
        Action<string> error,
        Func<IReadOnlyList<string>>? outputs = null,
        Func<IReadOnlyList<PresetRecordingStreamStatus>>? streams = null,
        IInputChannelReader? inputs = null) : IDisposable
    {
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
        private PresetControllerRun? _active;
        public WindowTarget? Target => _active?.Target;
        public WindowPreset? ActivePreset => _active?.Preset;
        public PixelRect? Region => _active?.Region;
        public string? Name => ActivePreset?.Name;
        public string? State => _active is { } owner ? owner.Script is not null ? "script" : owner.Machine.State : null;

        public string? LastError => _active is { } owner
            ? owner.Failure ?? owner.ScriptFailure?.Message ?? owner.Script?.LastError ?? owner.Machine.LastError
            : null;

        public PresetRuntime? Runtime => _active is { } owner ? owner.Script?.Runtime ?? owner.Machine.Runtime : null;
        public PresetDebugger? Debugger => _active?.Debugger;
        public string? ParameterSession => _active?.ParameterSession;

        public void NotifyRecordingChanged() => _active?.Script?.NotifyParameterWait();

        public void UpdateParameters(string session, IReadOnlyDictionary<string, object?> changes)
        {
            if (_active is not { } owner || owner.ParameterSession != session || !IsCurrent(owner) ||
                owner.Preset.Program is null || owner.ScriptTask?.IsCompleted == true)
                throw new InvalidOperationException(UiText.Get("CommandNotReady"));
            owner.Parameters.Update(changes);
        }

        public void Signal(string name)
        {
            if (_active is not { } owner) return;
            owner.Signals.Add(name);
            owner.Script?.Signal(name);
        }

        public void Dispose()
        {
            var owner = _active;
            _active = null;
            _timer.Stop();
            _timer.Tick -= Tick;
            if (owner is null) return;
            owner.Clock.Reset();
            try
            {
                if (owner.Debugger is { } debugger)
                {
                    debugger.Changed -= owner.DebugChanged;
                    debugger.Complete();
                }
            }
            finally
            {
                owner.Retire();
            }
        }

        public void Activate(WindowPreset preset, PresetDebugger? debugger = null,
            IReadOnlyDictionary<string, object?>? parameters = null)
        {
            if (DesktopCapture.UsesSourceSelection && (preset.Layout is { } layout
                    ? layout.Layers.Any(layer => !layer.Absolute && layer.Media is null)
                    : !preset.Absolute))
                throw new PlatformNotSupportedException(UiText.Get("PlatformWindowMatchingUnavailable"));
            Dispose();
            var owner = new PresetControllerRun(preset, debugger);
            if (parameters is not null) owner.Parameters.Update(parameters);
            _active = owner;
            owner.DebugChanged = () => DebugChanged(owner);
            if (debugger is not null) debugger.Changed += owner.DebugChanged;
            owner.Clock.Start();
            _timer.Tick += Tick;
            _timer.Start();
            Tick(null, EventArgs.Empty);
        }

        private bool IsCurrent(PresetControllerRun owner) =>
            ReferenceEquals(owner, _active) && !owner.Token.IsCancellationRequested;

        private void EnsureCurrent(PresetControllerRun owner)
        {
            if (!IsCurrent(owner)) throw new OperationCanceledException(owner.Token);
        }

        private void DebugChanged(PresetControllerRun owner)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => DebugChanged(owner));
                return;
            }

            if (!IsCurrent(owner)) return;
            if (owner.Debugger?.Paused == true) owner.Clock.Stop();
            else owner.Clock.Start();
        }

        private void Tick(object? sender, EventArgs args)
        {
            if (_active is not { Polling: false } owner) return;
            owner.Polling = true;
            owner.Acquire();
            _ = PollAsync(owner);
        }

        private async Task ExecuteAsync(PresetControllerRun owner, PresetAction action)
        {
            EnsureCurrent(owner);
            await execute(action, owner.Region);
            EnsureCurrent(owner);
        }

        private PresetContext Observe(PresetControllerRun owner)
        {
            EnsureCurrent(owner);
            var status = recording();
            EnsureCurrent(owner);
            var observed = owner.Target;
            var state = observed is null
                ? null
                : new PresetWindowState(observed.Handle, observed.Title,
                    owner.Preset.ClientArea ? observed.Client : observed.Bounds);
            return new PresetContext(state, status.Recording, status.Busy, 0, status.RecordingElapsed,
                owner.Preset.Constants, Outputs: outputs?.Invoke().ToArray(), Streams: streams?.Invoke());
        }

        private async Task RunScriptAsync(PresetControllerRun owner, PresetScriptSession session)
        {
            try
            {
                await Task.Run(() => session.RunAsync(owner.Token), CancellationToken.None);
            }
            catch (OperationCanceledException) when (!IsCurrent(owner))
            {
            }
            catch (Exception ex)
            {
                // Retired runs are still observed, but cannot report errors against their replacement.
                owner.ScriptFailure = ex;
            }
            finally
            {
                owner.Release();
            }
        }

        private async Task PollAsync(PresetControllerRun owner)
        {
            var preset = owner.Preset;
            var machine = owner.Machine;
            try
            {
                var needsWindows = !preset.Absolute &&
                                   preset.Layout?.Layers.All(layer => layer.Media is not null || layer.Absolute) !=
                                   true;
                var windows = needsWindows ? await Task.Run(WindowLocator.List, owner.Token) : [];
                EnsureCurrent(owner);
                var current = WindowLocator.Find(preset, windows, owner.Previous?.Handle ?? 0);
                var old = owner.Previous;
                owner.Target = current;
                if (preset.Layout is { } layout) owner.Region = new PixelRect(0, 0, layout.Width, layout.Height);
                else if (preset.Absolute) owner.Region = preset.Resolve(default, default, machine.Values);
                else if (current is not null &&
                         (owner.Region is null || preset.Follow || current.Handle != old?.Handle))
                {
                    try
                    {
                        owner.Region = preset.Resolve(current.Bounds, current.Client, machine.Values);
                    }
                    catch (Exception ex) when (ex is ArgumentException or OverflowException)
                    {
                        owner.Region = null;
                    }
                }

                if (owner.Region is not null) await ExecuteAsync(owner, new PresetAction("follow"));
                EnsureCurrent(owner);
                if (old is not null && current?.Handle != old.Handle && WindowLocator.IsWindow(old.Handle) == 0)
                    Signal("closed");
                if (old != current) owner.Script?.NotifyParameterWait();
                owner.Previous = current;
                var signals = owner.Signals.ToArray();
                owner.Signals.Clear();
                if (preset.Program is not null)
                {
                    if (owner.ScriptTask is null)
                    {
                        var session = new PresetScriptSession(preset,
                            async () => await Dispatcher.UIThread.InvokeAsync(() => Observe(owner)),
                            async action => await Dispatcher.UIThread.InvokeAsync(() => ExecuteAsync(owner, action)),
                            owner.Debugger, isCurrent: () => IsCurrent(owner),
                            mediaService: new MediaProcessingService(), parameters: owner.Parameters,
                            imageService: new ImageCompositionService(), inputReader: inputs);
                        owner.Script = session;
                        owner.Acquire();
                        owner.ScriptTask = RunScriptAsync(owner, session);
                    }
                    else if (owner.ScriptTask.IsCompleted)
                    {
                        await owner.ScriptTask;
                        EnsureCurrent(owner);
                        _timer.Stop();
                        var scriptError = owner.ScriptFailure?.Message ?? owner.Script?.LastError;
                        if (scriptError is not null) error(scriptError);
                    }

                    return;
                }

                await machine.StepAsync(owner.Clock.Elapsed.TotalSeconds, () => Observe(owner), signals,
                    action => ExecuteAsync(owner, action), owner.Token);
                EnsureCurrent(owner);
                if (machine.LastError is { } message) error(message);
                if (IsCurrent(owner) && machine.Finished) _timer.Stop();
            }
            catch (OperationCanceledException) when (!IsCurrent(owner))
            {
            }
            catch (Exception ex)
            {
                if (!IsCurrent(owner)) return;
                owner.Failure = ex.Message;
                _timer.Stop();
                try
                {
                    owner.Debugger?.Complete(ex.Message);
                    if (!IsCurrent(owner)) return;
                    error(ex.Message);
                    if (!IsCurrent(owner)) return;
                    await ExecuteAsync(owner, new PresetAction("cancel"));
                }
                catch (Exception)
                {
                    // LastError retains the failure even if reporting or cleanup itself fails.
                }
            }
            finally
            {
                owner.Polling = false;
                owner.Release();
            }
        }
    }
}

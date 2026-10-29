namespace RitsukageGif.Scripting
{
    public sealed partial class PresetScriptSession
    {
        internal PresetDebugger? DebugSession => debugger;
        internal string SourceDirectory => Path.GetDirectoryName(preset.SourceFile) ?? Environment.CurrentDirectory;

        internal async ValueTask<object?> ExecuteBoundAction(string action, PresetRecordingSettings? settings,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await ExecuteActionAsync(new(action, Settings: settings));
            return null;
        }

        internal async ValueTask<object?> WaitBound(IReadOnlyList<object?> args, CancellationToken token)
        {
            await WaitAsync(Seconds(args[0]), token);
            return null;
        }

        internal async ValueTask<object?> StartManyBound(IReadOnlyList<PresetRecordingOutput> outputs,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await StartOutputsAsync(outputs, token);
            return null;
        }

        internal async ValueTask<object?> WaitUntilBound(IReadOnlyList<object?> args, CancellationToken token)
        {
            var deadline = PresetMotion.Now + Seconds(args[1]);
            while (!ScriptEngine.Truth(await _engine!.InvokeAsync(args[0], [], token)))
            {
                if (PresetMotion.Now >= deadline) throw new TimeoutException(UiText.Get("DslAwaitTimeout"));
                await WaitAsync(.02, token);
            }

            return null;
        }

        internal async ValueTask<object?> StatusBound(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Context(await observe());
        }

        internal object? BreakpointBound()
        {
            debugger?.Pause();
            return null;
        }

        internal object? UpdateLayerBound(string operation, IReadOnlyList<object?> args)
        {
            UpdateLayer(operation, args);
            foreach (var route in _inheritedOutputScenes) route.UpdateLayerBound(operation, args);
            return null;
        }

        internal object ReadLayerBound(string name)
        {
            var definition = Layer(name);
            var state = _layers.GetValueOrDefault(name) ?? new PresetLayerState();
            var rect = state.Motion?.At(Runtime.Now) ?? state.Placement ?? definition.Placement;
            return new Dictionary<string, object?>
            {
                ["x"] = (double)rect.X, ["y"] = (double)rect.Y, ["width"] = (double)rect.Width,
                ["height"] = (double)rect.Height, ["visible"] = state.Visible, ["opacity"] = state.Opacity,
                ["effects"] = PixelEffectsBinding.Values(state.Effects),
                ["playback"] = PlaybackValue(definition.Media, state.Playback),
            };
        }

        internal object? SubscribeBound(string name, object? handler)
        {
            if (!_handlers.TryGetValue(name, out var handlers)) _handlers[name] = handlers = [];
            handlers.Add(handler);
            return null;
        }

        internal object? UnsubscribeBound(string name)
        {
            _handlers.Remove(name);
            return null;
        }

        internal object? EmitBound(string name)
        {
            _events.Enqueue(name);
            return null;
        }

        internal async ValueTask<object?> PumpBound(CancellationToken token)
        {
            await PumpAsync(token);
            return null;
        }
    }
}

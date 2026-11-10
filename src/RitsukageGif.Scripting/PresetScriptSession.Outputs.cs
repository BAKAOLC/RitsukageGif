namespace RitsukageGif.Scripting
{
    public sealed partial class PresetScriptSession
    {
        private readonly SemaphoreSlim _outputStartGate = new(1, 1);
        private List<PresetScriptSession> _inheritedOutputScenes = [];
        private List<PresetScriptSession> _pendingInheritedOutputScenes = [];
        private Dictionary<string, PresetScriptSession> _outputScenes = new(StringComparer.OrdinalIgnoreCase);

        private async Task StartOutputsAsync(IReadOnlyList<PresetRecordingOutput> outputs, CancellationToken token)
        {
            await _outputStartGate.WaitAsync(token);
            try
            {
                var playbackEpoch = PresetMotion.Now;
                InitializePlayback(playbackEpoch);
                var prepared = new List<(PresetRecordingOutput Output, WindowPreset Preset, bool Inherit)>();
                var catalogs = new Dictionary<string, IReadOnlyList<WindowPreset>>(OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);
                foreach (var output in outputs)
                {
                    token.ThrowIfCancellationRequested();
                    var source = preset;
                    var useCurrent = output.PresetFile is null && output.PresetName is null;
                    if (!useCurrent)
                    {
                        var file = output.PresetFile ?? preset.SourceFile ??
                            throw new ArgumentException(UiText.Get("ScriptTypeError"));
                        if (!catalogs.TryGetValue(file, out var catalog))
                            catalogs[file] = catalog = PresetCatalog.Load([file]);
                        source = output.PresetName is { } name
                            ? catalog.FirstOrDefault(item => item.Name == name)
                            : catalog.Count == 1
                                ? catalog[0]
                                : null;
                        if (source is null) throw new ArgumentException(UiText.Get("ScriptTypeError"));
                    }

                    prepared.Add((output, source, useCurrent));
                }

                var scenes = new Dictionary<string, PresetScriptSession>(StringComparer.OrdinalIgnoreCase);
                var inherited = new List<PresetScriptSession>();
                var resolved = new List<PresetRecordingOutput>(outputs.Count);
                lock (Runtime.StateGate)
                {
                    foreach (var entry in prepared)
                    {
                        var scene = new PresetScriptSession(entry.Preset, observe, execute);
                        scene.Runtime.ShareClock(Runtime);
                        if (entry.Inherit)
                        {
                            scene._values.Clear();
                            foreach (var value in _values) scene._values.Add(value.Key, value.Value);
                            foreach (var layer in _layers) scene._layers.Add(layer.Key, layer.Value);
                            inherited.Add(scene);
                        }

                        scene.InitializePlayback(playbackEpoch);
                        scenes.Add(entry.Output.Name, scene);
                        resolved.Add(entry.Output with { Preset = entry.Preset, Runtime = scene.Runtime });
                    }

                    _pendingInheritedOutputScenes = inherited;
                }

                try
                {
                    await ExecuteActionAsync(new("startMany", Outputs: resolved));
                }
                catch
                {
                    lock (Runtime.StateGate) _pendingInheritedOutputScenes = [];
                    throw;
                }

                lock (Runtime.StateGate)
                {
                    _outputScenes = scenes;
                    _inheritedOutputScenes = inherited;
                    _pendingInheritedOutputScenes = [];
                }
            }
            finally
            {
                _outputStartGate.Release();
            }
        }

        internal object? SetOutputLayerBound(string output, string name, Dictionary<string, object?> options)
        {
            lock (Runtime.StateGate) return OutputScene(output).SetLayerBound(name, options);
        }

        internal object? UpdateOutputLayerBound(string output, string operation, IReadOnlyList<object?> arguments)
        {
            lock (Runtime.StateGate) return OutputScene(output).UpdateLayerBound(operation, arguments);
        }

        internal object ReadOutputLayerBound(string output, string name)
        {
            lock (Runtime.StateGate) return OutputScene(output).ReadLayerBound(name);
        }

        private PresetScriptSession OutputScene(string name)
        {
            lock (Runtime.StateGate)
                return _outputScenes.TryGetValue(name, out var scene)
                    ? scene
                    : throw new ArgumentException(UiText.Format("DslUnknownName", name));
        }

        internal async ValueTask<object?> OutputsBound(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return StreamValues(await observe());
        }

        private static List<object?> StreamValues(PresetContext context)
        {
            return
            [
                .. (context.Streams ?? [])
                .Select(object? (stream) => new Dictionary<string, object?>
                {
                    ["name"] = stream.Name, ["state"] = stream.State, ["captured"] = (double)stream.Captured,
                    ["encoded"] = (double)stream.Encoded, ["elapsed"] = stream.Elapsed, ["path"] = stream.Path,
                    ["error"] = stream.Error,
                }),
            ];
        }
    }
}

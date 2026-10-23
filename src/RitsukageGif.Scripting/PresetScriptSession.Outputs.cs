namespace RitsukageGif.Scripting
{
    public sealed partial class PresetScriptSession
    {
        private Dictionary<string, PresetScriptSession> _outputScenes = new(StringComparer.OrdinalIgnoreCase);
        private List<PresetScriptSession> _inheritedOutputScenes = [];

        private async Task StartOutputsAsync(IReadOnlyList<PresetRecordingOutput> outputs, CancellationToken token)
        {
            var playbackEpoch = PresetMotion.Now;
            InitializePlayback(playbackEpoch);
            var scenes = new Dictionary<string, PresetScriptSession>(StringComparer.OrdinalIgnoreCase);
            var inherited = new List<PresetScriptSession>();
            var resolved = new List<PresetRecordingOutput>(outputs.Count);
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

                var scene = new PresetScriptSession(source, observe, execute);
                scene.Runtime.ShareClock(Runtime);
                if (useCurrent)
                {
                    scene._values.Clear();
                    foreach (var entry in _values) scene._values.Add(entry.Key, entry.Value);
                    foreach (var entry in _layers) scene._layers.Add(entry.Key, entry.Value);
                    inherited.Add(scene);
                }

                scene.InitializePlayback(playbackEpoch);
                scenes.Add(output.Name, scene);
                resolved.Add(output with { Preset = source, Runtime = scene.Runtime });
            }

            await ExecuteActionAsync(new PresetAction("startMany", Outputs: resolved));
            _outputScenes = scenes;
            _inheritedOutputScenes = inherited;
        }

        internal PresetScriptSession OutputScene(string name) => _outputScenes.TryGetValue(name, out var scene)
            ? scene
            : throw new ArgumentException(UiText.Format("DslUnknownName", name));

        internal async ValueTask<object?> OutputsBound(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return StreamValues(await observe());
        }

        private static List<object?> StreamValues(PresetContext context) => (context.Streams ?? [])
            .Select(stream => (object?)new Dictionary<string, object?>
            {
                ["name"] = stream.Name, ["state"] = stream.State, ["captured"] = (double)stream.Captured,
                ["encoded"] = (double)stream.Encoded, ["elapsed"] = stream.Elapsed, ["path"] = stream.Path,
                ["error"] = stream.Error
            }).ToList();
    }
}

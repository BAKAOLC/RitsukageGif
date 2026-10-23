namespace RitsukageGif.Scripting
{
    public sealed record PresetRuntimeSnapshot(
        IReadOnlyDictionary<string, object> Variables,
        IReadOnlyDictionary<string, PresetLayerState> Layers);
}

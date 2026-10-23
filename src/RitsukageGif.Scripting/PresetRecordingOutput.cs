namespace RitsukageGif.Scripting
{
    public sealed record PresetRecordingOutput(
        string Name,
        string Path,
        string? PresetFile = null,
        string? PresetName = null,
        PresetRecordingSettings? Settings = null)
    {
        public WindowPreset? Preset { get; init; }
        public PresetRuntime? Runtime { get; init; }
    }
}

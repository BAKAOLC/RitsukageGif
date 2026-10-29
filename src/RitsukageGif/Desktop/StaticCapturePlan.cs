namespace RitsukageGif.Desktop
{
    internal sealed record StaticCapturePlan(
        RecordingOptions Options,
        WindowPreset? Preset,
        PresetRuntime? Runtime,
        PixelRect[] Monitors,
        int ScalePercent);
}

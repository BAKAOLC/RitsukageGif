namespace RitsukageGif.Scripting
{
    public sealed record PresetLayer(
        string Name,
        string? Process,
        string Title,
        bool ClientArea,
        IReadOnlyList<PresetExpression> Area,
        PixelRect Placement,
        PresetShape Shape,
        CaptureSourceKind Source,
        PresetFallback Missing,
        bool Follow = true,
        bool Absolute = false,
        PresetMediaSource? Media = null);
}

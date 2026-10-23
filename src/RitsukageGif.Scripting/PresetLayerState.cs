namespace RitsukageGif.Scripting
{
    public sealed record PresetLayerState(
        PixelRect? Placement = null,
        bool Visible = true,
        double Opacity = 1,
        PixelRect? Crop = null,
        PresetMotion? Motion = null,
        IReadOnlyList<PixelEffect>? Effects = null,
        PresetPlaybackState? Playback = null);
}

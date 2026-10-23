namespace RitsukageGif.Scripting
{
    public sealed record PresetPlaybackState(
        double Position,
        double Speed,
        bool Paused,
        bool Loop,
        bool HoldLast,
        double UpdatedAt);
}

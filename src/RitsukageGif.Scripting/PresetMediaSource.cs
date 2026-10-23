namespace RitsukageGif.Scripting
{
    public sealed record PresetMediaSource(
        string File,
        bool Loop = true,
        double Speed = 1,
        double Position = 0,
        bool Ffmpeg = false,
        int Width = 0,
        int Height = 0,
        bool Live = false,
        bool HoldLast = true,
        double StaleAfter = 5,
        bool Channel = false,
        double Delay = 0);
}

namespace RitsukageGif.Core
{
    public sealed record FfmpegInputOptions(string Input)
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public bool Loop { get; init; } = true;
        public double PlaybackRate { get; init; } = 1;
        public double PositionSeconds { get; init; }
        public bool Live { get; init; }
        public bool HoldLastFrame { get; init; }
        public double StaleAfterSeconds { get; init; } = 5;
    }
}
